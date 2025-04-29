using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class AmdGpuSensorDataProvider : ISensorDataProvider
    {
        public string Name => "gpu.amd";
        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;

        private readonly string _hwmonPath = "/sys/class/hwmon";
        private (string, float)[] _sensorData;

        public AmdGpuSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _provideSensors = provideSensors;
            _sensorData = new (string, float)[_provideSensors.Count];
        }

        public bool GetSensorData(out (string, float)[] data)
        {

            try
            {
                int i = 0;
                string[] hwmonDirs = Directory.GetDirectories(_hwmonPath);
                foreach (string hwmonDir in hwmonDirs)
                {
                    // Check if this hwmon directory likely belongs to an AMD GPU
                    string nameFile = Path.Combine(hwmonDir, "name");
                    if (File.Exists(nameFile))
                    {
                        string name = File.ReadAllText(nameFile).Trim();
                        if (name.ToLowerInvariant().Contains("amd") || name.ToLowerInvariant().Contains("radeon"))
                        {
                            string gpuIndex = Regex.Match(hwmonDir, @"hwmon(\d+)").Groups[1].Value;
                            string prefix = string.IsNullOrEmpty(gpuIndex) ? "GPU" : $"GPU {gpuIndex}";
                            string sensorName = $"{prefix}.power";

                            // Power Usage
                            string powerFile = Path.Combine(hwmonDir, "power1_average");
                            if (File.Exists(powerFile) && _provideSensors.Contains(sensorName))
                            {
                                string powerString = File.ReadAllText(powerFile).Trim();
                                if (double.TryParse(powerString, out double powerMicroWatts))
                                {
                                    _sensorData[i].Item1 = sensorName;
                                    _sensorData[i].Item2 = (float)(powerMicroWatts / 1_000_000.0);
                                    i++;
                                }
                            }

                            // Temperature
                            string[] tempInputFiles = Directory.GetFiles(hwmonDir, "temp*_input");
                            foreach (string tempFile in tempInputFiles)
                            {
                                string indexStr = Regex.Match(tempFile, @"temp(\d+)_input").Groups[1].Value;
                                string labelFile = tempFile.Replace("_input", "_label");
                                string tempName = $"{prefix} Temperature {indexStr} (°C)";

                                if (File.Exists(labelFile))
                                {
                                    string label = File.ReadAllText(labelFile).Trim();
                                    tempName = $"{prefix}.temp.{label}";
                                }
                                else if (tempFile.Contains("edge"))
                                {
                                    tempName = $"{prefix}.temp.edge";
                                }
                                else if (tempFile.Contains("junction"))
                                {
                                    tempName = $"{prefix}.temp.junction";
                                }
                                else if (tempFile.Contains("memory"))
                                {
                                    tempName = $"{prefix}.temp.memory";
                                }

                                string tempString = File.ReadAllText(tempFile).Trim();
                                if (int.TryParse(tempString, out int tempMilliCelsius) && _provideSensors.Contains(tempString))
                                {
                                    _sensorData[i].Item1 = tempName;
                                    _sensorData[i].Item2 = (float)(tempMilliCelsius / 1000.0);
                                    i++;
                                }
                            }

                            // Fan Speed (if available)
                            string[] fanInputFiles = Directory.GetFiles(hwmonDir, "fan*_input");
                            foreach (string fanFile in fanInputFiles)
                            {
                                string indexStr = Regex.Match(fanFile, @"fan(\d+)_input").Groups[1].Value;
                                string labelFile = fanFile.Replace("_input", "_label");
                                string fanName = $"{prefix}.fan.{indexStr}.rpm";

                                if (File.Exists(labelFile))
                                {
                                    string label = File.ReadAllText(labelFile).Trim();
                                    fanName = $"{prefix}.{label}.rpm";
                                }

                                string fanSpeedString = File.ReadAllText(fanFile).Trim();
                                if (int.TryParse(fanSpeedString, out int fanSpeedRpm) && _provideSensors.Contains(fanName))
                                {
                                    _sensorData[i].Item1 = fanName;
                                    _sensorData[i].Item2 = fanSpeedRpm;
                                    i++;
                                }
                            }

                            // GPU Usage (may not be directly available through standard hwmon)
                            // This often requires reading from other interfaces or using external tools.
                            // The following is a placeholder and might not work directly.
                            string utilizationFile = Path.Combine(hwmonDir, "gpu_busy_percent");
                            if (File.Exists(utilizationFile))
                            {
                                string utilizationString = File.ReadAllText(utilizationFile).Trim();
                                if (int.TryParse(utilizationString, out int utilizationPercent))
                                {
                                    _sensorData[i].Item1 = $"{prefix}.util";
                                    _sensorData[i].Item2 = utilizationPercent;
                                }
                            }
                        }
                    }
                }

                data = _sensorData;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading AMD GPU data: {ex.Message}");
                data = Array.Empty<(string, float)>();
                return false;
            }
        }
    }
}