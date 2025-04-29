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
        public string Name => "gpu.intel_gpu_top";
        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;

        private Process _process;
        private StreamReader _outputReader;
        private byte[] _outputBuffer; // Let the buffer resize dynamically if needed
        private int _outputBufferLength = 0;
        private bool _disposed = false;
        private (string, float)[] _sensorData;
        private static readonly char[] _lineSeparators = ['\n'];
        private static readonly char[] _csvSeparators = [','];
        private Dictionary<int, string> _dynamicHeaderMap = new Dictionary<int, string>();

        private bool _headersRead = false;

        public IntelGpuSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _provideSensors = provideSensors;
            _sensorData = new (string, float)[_provideSensors.Count];
            _outputBuffer = new byte[4096]; // Initial buffer size
            StartIntelGpuTop();
        }

        private void StartIntelGpuTop()
        {
            _process = new Process();
            _process.StartInfo.FileName = "/usr/bin/intel_gpu_top"; // Adjust path if necessary
            _process.StartInfo.Arguments = "-c -s 950"; // Request CSV and set interval to 950ms
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
                lock (_sensorData)
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
                                _dynamicHeaderMap[i] = headers[i];
                            }
                            _headersRead = true;
                        }
                        else if (headerLine != null)
                        {
                            // If the first line doesn't look like headers, we might be in a state where the process just started
                            return;
                        }
                    }

                    if (_headersRead && lines.Length > 0)
                    {
                        string[] values = lines.LastOrDefault().Split(_csvSeparators, StringSplitOptions.TrimEntries);

                        int i = 0;
                        foreach (var kvp in _dynamicHeaderMap)
                        {
                            if (kvp.Key < values.Length)
                            {
                                if (float.TryParse(values[kvp.Key], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && _provideSensors.Contains(kvp.Value))
                                {
                                    _sensorData[i].Item1 = kvp.Value;
                                    _sensorData[i].Item2 = value;
                                    i++;
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

        public bool GetSensorData(out (string, float)[] data)
        {
            lock (_sensorData)
            {
                data = _sensorData;
                return true;
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