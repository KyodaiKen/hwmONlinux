using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class IntelGpuSensorDataProvider : ISensorDataProvider, IDisposable
    {
        public string Name => "IntelGpuTop";
        public string FriendlyName { get; }

        private Process _process;
        private StreamReader _outputReader;
        private byte[] _outputBuffer; // Let the buffer resize dynamically if needed
        private int _outputBufferLength = 0;
        private bool _disposed = false;
        private SensorData _currentSensorData;
        private static readonly char[] _lineSeparators = ['\n'];
        private static readonly char[] _csvSeparators = [','];
        private Dictionary<int, string> _dynamicHeaderMap = new Dictionary<int, string>();
        private readonly Dictionary<string, string> _headerMapping = new Dictionary<string, string>()
        {
            { "Freq MHz req", "GPU Requested Frequency (MHz)" },
            { "Freq MHz act", "GPU Frequency (MHz)" },
            { "IRQ /s", "GPU IRQ Rate (/s)" },
            { "RC6 %", "GPU RC6 Residency (%)" },
            { "Power W gpu", "GPU Power (W)" },
            { "Power W pkg", "GPU Package Power (W)" },
            { "RCS %", "GPU Render/3D Utilization (%)" },
            { "BCS %", "GPU Blitter Utilization (%)" },
            { "VCS %", "GPU Video Engine Utilization (%)" },
            { "VECS %", "GPU Video Prostproc Utilization (%)" }
            // Add mappings for 'se' and 'wa' if you want to expose them
        };
        private readonly Dictionary<string, object> _sensorValues;
        private bool _headersRead = false;

        public IntelGpuSensorDataProvider(string friendlyName)
        {
            FriendlyName = friendlyName;
            _sensorValues = new Dictionary<string, object>();
            _currentSensorData = new SensorData { Values = _sensorValues }; // Use the pre-allocated dictionary
            _outputBuffer = new byte[4096]; // Initial buffer size
            StartIntelGpuTop();
        }

        private void StartIntelGpuTop()
        {
            _process = new Process();
            _process.StartInfo.FileName = "/usr/bin/intel_gpu_top"; // Adjust path if necessary
            _process.StartInfo.Arguments = "-c -s 800"; // Request CSV and set interval to 800ms
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            _process.EnableRaisingEvents = true; // For Exited event

            try
            {
                _process.Start();
                _outputReader = _process.StandardOutput;
                _process.BeginErrorReadLine(); // Still read errors
                _process.Exited += OnProcessExited;
                Task.Run(ReadOutputAsync); // Read output in a separate task
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting intel_gpu_top: {ex.Message}");
                // Consider how to handle this error
            }
        }

        private async Task ReadOutputAsync()
        {
            char[] charBuffer = new char[1024];
            int charsRead;
            while (_process != null && !_process.HasExited)
            {
                try
                {
                    charsRead = await _outputReader.ReadAsync(charBuffer, 0, charBuffer.Length);
                    if (charsRead > 0)
                    {
                        // Convert the char buffer to bytes and append to the output buffer
                        int bytesToWrite = Encoding.UTF8.GetBytes(charBuffer, 0, charsRead, _outputBuffer, _outputBufferLength);
                        _outputBufferLength += bytesToWrite;

                        // Process the buffer
                        ParseOutputBuffer();

                        // Resize the buffer if it's full and we haven't found a newline
                        if (_outputBufferLength == _outputBuffer.Length)
                        {
                            int lastNewline = Array.IndexOf<byte>(_outputBuffer, (byte)'\n', 0, _outputBufferLength);
                            if (lastNewline == -1)
                            {
                                // Resize the buffer to accommodate longer lines
                                Array.Resize(ref _outputBuffer, _outputBuffer.Length * 2);
                            }
                            else
                            {
                                // Shift remaining data to the beginning
                                int remainingLength = _outputBufferLength - (lastNewline + 1);
                                Buffer.BlockCopy(_outputBuffer, lastNewline + 1, _outputBuffer, 0, remainingLength);
                                _outputBufferLength = remainingLength;
                            }
                        }
                    }
                    else if (_process.HasExited)
                    {
                        break;
                    }
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error reading intel_gpu_top output: {ex.Message}");
                    break;
                }
            }
        }

        private void ParseOutputBuffer()
        {
            if (_outputBufferLength == 0)
                return;

            string bufferAsString = Encoding.UTF8.GetString(_outputBuffer, 0, _outputBufferLength);
            string[] lines = bufferAsString.Split(_lineSeparators, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 0)
            {
                lock (_currentSensorData)
                {
                    if (!_headersRead)
                    {
                        string headerLine = lines.FirstOrDefault();
                        if (headerLine != null && headerLine.Contains(_csvSeparators[0]))
                        {
                            string[] headers = headerLine.Split(_csvSeparators, StringSplitOptions.TrimEntries);
                            _dynamicHeaderMap.Clear();
                            for (int i = 0; i < headers.Length; i++)
                            {
                                if (_headerMapping.ContainsKey(headers[i]))
                                {
                                    _dynamicHeaderMap[i] = headers[i];
                                }
                            }
                            _headersRead = true;
                            lines = lines.Skip(1).ToArray(); // Skip the header line for data processing
                        }
                        else if (headerLine != null)
                        {
                            // If the first line doesn't look like headers, we might be in a state where the process just started
                            return;
                        }
                    }

                    if (_headersRead && lines.Length > 0)
                    {
                        _sensorValues.Clear();
                        string lastLine = lines.LastOrDefault();
                        if (lastLine != null)
                        {
                            string[] values = lastLine.Split(_csvSeparators, StringSplitOptions.TrimEntries);

                            foreach (var kvp in _dynamicHeaderMap)
                            {
                                int index = kvp.Key;
                                string header = kvp.Value;

                                if (index < values.Length)
                                {
                                    if (_headerMapping.TryGetValue(header, out string mappedName) &&
                                        double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                                    {
                                        _sensorValues[mappedName] = value;
                                    }
                                }
                            }
                        }
                    }
                }
                _outputBufferLength = 0; // Reset the buffer length
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            Console.WriteLine($"intel_gpu_top process exited with code: {_process.ExitCode}");
            // Optionally restart the process or handle the termination
        }

        public SensorData GetSensorData()
        {
            lock (_currentSensorData)
            {
                return _currentSensorData;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _process?.Kill();
                    _process?.Dispose();
                    _outputReader?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~IntelGpuSensorDataProvider()
        {
            Dispose(disposing: false);
        }
    }
}