using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class IntelGpuSensorDataProvider : ISensorDataProvider, IDisposable
    {
        public string Name => "IntelGpuTop";
        public string FriendlyName { get; }

        private Process _process;
        private StreamReader _outputReader;
        private StringBuilder _outputBuffer = new StringBuilder();
        private bool _disposed = false;
        private SensorData _currentSensorData;

        public IntelGpuSensorDataProvider(string friendlyName)
        {
            FriendlyName = friendlyName;
            _currentSensorData = new SensorData { Values = new Dictionary<string, object>() };
            StartIntelGpuTop();
        }

        private void StartIntelGpuTop()
        {
            _process = new Process();
            _process.StartInfo.FileName = "/usr/bin/intel_gpu_top"; // Adjust path if necessary
            _process.StartInfo.Arguments = "-J -s 500";
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            _process.EnableRaisingEvents = true; // For Exited event

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.OutputDataReceived += OnOutputDataReceived;
                _process.Exited += OnProcessExited;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting intel_gpu_top: {ex.Message}");
                // Consider how to handle this error - perhaps retry or mark as unavailable
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e?.Data != null)
            {
                _outputBuffer.Append(e.Data);
                // Attempt to parse the buffer
                ParseOutputBuffer();
            }
        }

        private void ParseOutputBuffer()
        {
            string currentBuffer = _outputBuffer.ToString();
            int startIndex = -1;
            int endIndex = -1;
            int openBraceCount = 0;

            for (int i = 0; i < currentBuffer.Length; i++)
            {
                if (currentBuffer[i] == '{')
                {
                    if (openBraceCount == 0)
                    {
                        startIndex = i;
                    }
                    openBraceCount++;
                }
                else if (currentBuffer[i] == '}')
                {
                    openBraceCount--;
                    if (openBraceCount == 0 && startIndex != -1)
                    {
                        endIndex = i;
                        break; // Found the first complete JSON object
                    }
                }
            }

            if (startIndex != -1 && endIndex != -1)
            {
                string validJsonSegment = currentBuffer.Substring(startIndex, endIndex - startIndex + 1).Trim();

                // Remove the processed JSON segment from the buffer
                _outputBuffer.Remove(0, endIndex + 1);

                // Handle potential leading comma in the remaining buffer
                if (_outputBuffer.Length > 0 && _outputBuffer[0] == ',')
                {
                    _outputBuffer.Remove(0, 1);
                }

                try
                {
                    using (JsonDocument document = JsonDocument.Parse(validJsonSegment))
                    {
                        lock (_currentSensorData)
                        {
                            _currentSensorData.Values.Clear();

                            if (document.RootElement.TryGetProperty("frequency", out var frequencyElement))
                            {
                                if (frequencyElement.TryGetProperty("actual", out var actualFreqElement) &&
                                    actualFreqElement.TryGetDouble(out double actualFreq))
                                {
                                    _currentSensorData.Values["GPU Frequency (MHz)"] = actualFreq;
                                }
                            }

                            if (document.RootElement.TryGetProperty("engines", out var enginesElement))
                            {
                                foreach (var engineProperty in enginesElement.EnumerateObject())
                                {
                                    if (engineProperty.Value.TryGetProperty("busy", out var busyElement) &&
                                        busyElement.TryGetDouble(out double busyPercentage))
                                    {
                                        _currentSensorData.Values[$"GPU {engineProperty.Name} Utilization (%)"] = busyPercentage;
                                    }
                                }
                            }

                            if (document.RootElement.TryGetProperty("power", out var powerElement))
                            {
                                if (powerElement.TryGetProperty("GPU", out var gpuPowerElement) &&
                                    gpuPowerElement.TryGetDouble(out double gpuPower))
                                {
                                    _currentSensorData.Values["GPU Power (W)"] = gpuPower;
                                }
                                if (powerElement.TryGetProperty("Package", out var packageElement) &&
                                    packageElement.TryGetDouble(out double packagePower))
                                {
                                    _currentSensorData.Values["GPU Package Power (W)"] = packagePower;
                                }
                            }

                            if (document.RootElement.TryGetProperty("rc6", out var rc6Element))
                            {
                                if (rc6Element.TryGetProperty("value", out var valueElement) &&
                                    valueElement.TryGetDouble(out double rc6Residency))
                                {
                                    _currentSensorData.Values["GPU RC6 Residency (%)"] = rc6Residency;
                                }
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Warning: Error parsing JSON from intel_gpu_top: {ex.Message} - Output: '{validJsonSegment}'");
                }

                // Process any remaining complete JSON objects in the buffer
                ParseOutputBuffer();
            }
            // If no complete JSON object is found, we wait for more data
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
                return new SensorData { Values = new Dictionary<string, object>(_currentSensorData.Values) };
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _process?.Kill();
                    _process?.Dispose();
                    _outputReader?.Dispose();
                }

                // Dispose unmanaged resources (if any)
                _disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~IntelGpuSensorDataProvider()
        {
            // Finalizer calls Dispose(false)
            Dispose(disposing: false);
        }
    }
}