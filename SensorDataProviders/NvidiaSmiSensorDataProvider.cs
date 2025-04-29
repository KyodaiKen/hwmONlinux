using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace HwMonLinux
{
    public class NvidiaSmiSensorDataProvider : ISensorDataProvider
    {
        public string Name => "gpu.nvidia-smi";
        public string FriendlyName { get; }
        private readonly List<string> _provideSensors;

        private (string, float)[] _sensorData;

        public NvidiaSmiSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _provideSensors = provideSensors;
            _sensorData = new (string, float)[_provideSensors.Count];
        }

        public bool GetSensorData(out (string, float)[] data)
        {
            try
            {
                string queryArguments = $"--query-gpu={string.Join(",", _provideSensors)} --format=csv,noheader,nounits";
                string output;
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "nvidia-smi";
                    process.StartInfo.Arguments = queryArguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                }

                var lines = output.Trim().Split('\n');
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var values = lines[i].Split(',').Select(v => v.Trim()).ToList();
                    for (int j = 0; j < _provideSensors.Count && j < values.Count; j++)
                    {
                        string rawSensorName = _provideSensors[j];

                        if (float.TryParse(values[j], NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                        {
                            _sensorData[j].Item1 = _provideSensors[j];
                            _sensorData[j].Item2 = floatValue;
                        }
                        else if (int.TryParse(values[j], NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                        {
                            _sensorData[j].Item1 = _provideSensors[j];
                            _sensorData[j].Item2 = intValue;
                        }
                    }
                    values.Clear();
                }
                data = _sensorData;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading NVIDIA SMI data: {ex.Message}");
                data = Array.Empty<(string, float)>();
                return false;
            }
        }
    }
}