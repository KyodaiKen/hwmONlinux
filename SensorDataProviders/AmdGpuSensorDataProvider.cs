using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class AmdGpuSensorDataProvider : ISensorDataProvider
    {
        public string Name => "AmdGpu";
        public string FriendlyName { get; }
        private readonly string _hwmonPath = "/sys/class/hwmon";

        public AmdGpuSensorDataProvider(string friendlyName)
        {
            FriendlyName = friendlyName;
        }

        public SensorData GetSensorData()
        {
            var sensorValues = new Dictionary<string, object>();
            try
            {
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

                            // Power Usage
                            string powerFile = Path.Combine(hwmonDir, "power1_average");
                            if (File.Exists(powerFile))
                            {
                                string powerString = File.ReadAllText(powerFile).Trim();
                                if (double.TryParse(powerString, out double powerMicroWatts))
                                {
                                    sensorValues[$"{prefix} Power (W)"] = powerMicroWatts / 1_000_000.0;
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
                                    tempName = $"{prefix} {label} Temperature (°C)";
                                }
                                else if (tempFile.Contains("edge"))
                                {
                                    tempName = $"{prefix} Edge Temperature (°C)";
                                }
                                else if (tempFile.Contains("junction"))
                                {
                                    tempName = $"{prefix} Junction Temperature (°C)";
                                }
                                else if (tempFile.Contains("memory"))
                                {
                                    tempName = $"{prefix} Memory Temperature (°C)";
                                }

                                string tempString = File.ReadAllText(tempFile).Trim();
                                if (int.TryParse(tempString, out int tempMilliCelsius))
                                {
                                    sensorValues[tempName] = tempMilliCelsius / 1000.0;
                                }
                            }

                            // Fan Speed (if available)
                            string[] fanInputFiles = Directory.GetFiles(hwmonDir, "fan*_input");
                            foreach (string fanFile in fanInputFiles)
                            {
                                string indexStr = Regex.Match(fanFile, @"fan(\d+)_input").Groups[1].Value;
                                string labelFile = fanFile.Replace("_input", "_label");
                                string fanName = $"{prefix} Fan {indexStr} RPM";

                                if (File.Exists(labelFile))
                                {
                                    string label = File.ReadAllText(labelFile).Trim();
                                    fanName = $"{prefix} {label} Fan RPM";
                                }

                                string fanSpeedString = File.ReadAllText(fanFile).Trim();
                                if (int.TryParse(fanSpeedString, out int fanSpeedRpm))
                                {
                                    sensorValues[fanName] = fanSpeedRpm;
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
                                    sensorValues[$"{prefix} Utilization (%)"] = utilizationPercent;
                                }
                            }
                        }
                    }
                }

                return new SensorData { Values = sensorValues };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading AMD GPU data: {ex.Message}");
                return null;
            }
        }
    }
}