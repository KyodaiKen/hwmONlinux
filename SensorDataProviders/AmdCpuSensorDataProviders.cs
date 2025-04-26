using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;

namespace HwMonLinux
{
    public class AmdPackagePowerSensorDataProvider : ISensorDataProvider
    {
        public string Name => "AmdPackagePower";
        public string FriendlyName { get; }
        private readonly string _powerFilePath;
        private double _previousEnergyJoules = 0;
        private DateTime _previousReadTime = DateTime.MinValue;
        private SensorData _sensorData;

        public AmdPackagePowerSensorDataProvider(string friendlyName, string powerFilePath = "/sys/class/hwmon/hwmon*/power1_average")
        {
            FriendlyName = friendlyName;
            _powerFilePath = powerFilePath;

            // Check if any matching power file exists
            if (!Directory.GetFiles("/sys/class/hwmon", "power*_*average").Any(f => Regex.IsMatch(f, _powerFilePath.Replace("*", @"\d+"))))
            {
                Console.WriteLine($"Warning: AMD CPU power statistics file not found matching '{_powerFilePath}'. This provider might not function.");
            }

            _sensorData = new();
            _sensorData.Values = new();
        }

        public SensorData GetSensorData()
        {

            try
            {
                // Find the actual power file path
                string actualPowerFilePath = Directory.GetFiles("/sys/class/hwmon", "power*_*average")
                                                    .FirstOrDefault(f => Regex.IsMatch(f, _powerFilePath.Replace("*", @"\d+")));

                if (actualPowerFilePath != null && File.Exists(actualPowerFilePath))
                {
                    string powerString = File.ReadAllText(actualPowerFilePath).Trim();
                    if (double.TryParse(powerString, NumberStyles.Float, CultureInfo.InvariantCulture, out double currentPowerMicroWatts))
                    {
                        double currentPowerWatts = currentPowerMicroWatts / 1_000_000.0;
                        _sensorData.Values ["Package Power (W)"] = (float)currentPowerWatts;
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse power value from '{actualPowerFilePath}': '{powerString}'.");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: AMD CPU power statistics file not found matching '{_powerFilePath}'.");
                }

                return _sensorData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading AMD package power data from '{_powerFilePath}': {ex.Message}");
                return null;
            }
        }
    }

    public class AmdCpuTemperatureSensorDataProvider : ISensorDataProvider
    {
        public string Name => "AmdCpuTemperature";
        public string FriendlyName { get; }
        private readonly string _tempFilePathPattern;
        private SensorData _sensorData;

        public AmdCpuTemperatureSensorDataProvider(string friendlyName, string tempFilePathPattern = "/sys/class/hwmon/hwmon*/temp*_input")
        {
            FriendlyName = friendlyName;
            _tempFilePathPattern = tempFilePathPattern;

            if (!Directory.GetFiles("/sys/class/hwmon", "temp*_input").Any(f => Regex.IsMatch(f, _tempFilePathPattern.Replace("*", @"\d+"))))
            {
                Console.WriteLine($"Warning: AMD CPU temperature input files not found matching '{_tempFilePathPattern}'. This provider might not function.");
            }
        }

        public SensorData GetSensorData()
        {
            _sensorData ??= new();
            _sensorData.Values ??= new();

            try
            {
                string[] tempFiles = Directory.GetFiles("/sys/class/hwmon", "temp*_input")
                                            .Where(f => Regex.IsMatch(f, _tempFilePathPattern.Replace("*", @"\d+"))).ToArray();

                for (int i = 0; i < tempFiles.Length; i++)
                {
                    string tempFile = tempFiles[i];
                    string tempString = File.ReadAllText(tempFile).Trim();
                    if (int.TryParse(tempString, out int temperatureMilliCelsius))
                    {
                        double temperatureCelsius = temperatureMilliCelsius / 1000.0;

                        // Try to find a corresponding label file for a better name
                        string labelFile = tempFile.Replace("_input", "_label");
                        string sensorName = $"Core {i} Temperature (°C)";
                        if (File.Exists(labelFile))
                        {
                            string label = File.ReadAllText(labelFile).Trim();
                            sensorName = $"{label} Temperature (°C)";
                        }
                        else
                        {
                            // Try to infer a more descriptive name based on the directory
                            string hwmonDir = Directory.GetParent(tempFile)?.Name;
                            if (hwmonDir != null && hwmonDir.StartsWith("hwmon"))
                            {
                                string indexStr = Regex.Match(tempFile, @"temp(\d+)_input").Groups[1].Value;
                                if (!string.IsNullOrEmpty(indexStr))
                                {
                                    sensorName = $"CPU Core {indexStr} Temperature (°C)";
                                }
                                else if (tempFile.Contains("package"))
                                {
                                    sensorName = "CPU Package Temperature (°C)";
                                }
                            }
                        }
                        _sensorData.Values[sensorName] = (float)Math.Round(temperatureCelsius, 1);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse temperature value from '{tempFile}': '{tempString}'.");
                    }
                }

                return _sensorData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading AMD CPU temperature data from '{_tempFilePathPattern}': {ex.Message}");
                return null;
            }
        }
    }
}