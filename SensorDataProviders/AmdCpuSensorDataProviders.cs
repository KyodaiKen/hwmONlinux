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
        public string Name => "cpu.power.amd.package";
        public string FriendlyName { get; }
        private readonly string _powerFilePath;

        private readonly List<string> _provideSensors;

        private double _previousEnergyJoules = 0;
        private DateTime _previousReadTime = DateTime.MinValue;
        private (string, float)[] _sensorData;

        // ecample powerFilePath: "/sys/class/hwmon/hwmon*/power1_average"
        public AmdPackagePowerSensorDataProvider(string friendlyName, string powerFilePath, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _powerFilePath = powerFilePath;

            _provideSensors = provideSensors;

            // Check if any matching power file exists
            if (!Directory.GetFiles("/sys/class/hwmon", "power*_*average").Any(f => Regex.IsMatch(f, _powerFilePath.Replace("*", @"\d+"))))
            {
                Console.WriteLine($"Warning: AMD CPU power statistics file not found matching '{_powerFilePath}'. This provider might not function.");
            }

            _sensorData = new (string, float)[1];
        }

        public bool GetSensorData(out (string, float)[] data)
        {

            try
            {
                // Find the actual power file path
                string actualPowerFilePath = Directory.GetFiles("/sys/class/hwmon", "power*_*average")
                                                    .FirstOrDefault(f => Regex.IsMatch(f, _powerFilePath.Replace("*", @"\d+")));

                if (actualPowerFilePath != null && File.Exists(actualPowerFilePath))
                {
                    string powerString = File.ReadAllText(actualPowerFilePath).Trim();
                    if (double.TryParse(powerString, NumberStyles.Float, CultureInfo.InvariantCulture, out double currentPowerMicroWatts) && _provideSensors.Contains("temp.package"))
                    {
                        double currentPowerWatts = currentPowerMicroWatts / 1_000_000.0;
                        _sensorData[0].Item1 = "temp.package";
                        _sensorData[0].Item2 = (float)currentPowerWatts;
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

                data = _sensorData;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading AMD package power data from '{_powerFilePath}': {ex.Message}");
                data = Array.Empty<(string, float)>();
                return false;
            }
        }
    }

    public class AmdCpuTemperatureSensorDataProvider : ISensorDataProvider
    {
        public string Name => "cpu.temperature.amd";
        public string FriendlyName { get; }
        private readonly string _tempFilePathPattern;

        private readonly List<string> _provideSensors;

        private (string, float)[] _sensorData;

        // Sample tempFilePathPattern: /sys/class/hwmon/hwmon*/temp*_input
        public AmdCpuTemperatureSensorDataProvider(string friendlyName, string tempFilePathPattern, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _tempFilePathPattern = tempFilePathPattern;

            if (!Directory.GetFiles("/sys/class/hwmon", "temp*_input").Any(f => Regex.IsMatch(f, _tempFilePathPattern.Replace("*", @"\d+"))))
            {
                Console.WriteLine($"Warning: AMD CPU temperature input files not found matching '{_tempFilePathPattern}'. This provider might not function.");
            }

            _sensorData = new (string, float)[1];
        }

        public bool GetSensorData(out (string, float)[] data)
        {
            try
            {
                string[] tempFiles = Directory.GetFiles("/sys/class/hwmon", "temp*_input")
                                            .Where(f => Regex.IsMatch(f, _tempFilePathPattern.Replace("*", @"\d+"))).ToArray();

                for (int i = 0; i < tempFiles.Length; i++)
                {
                    string tempFile = tempFiles[i];
                    string tempString = File.ReadAllText(tempFile).Trim();
                    if (int.TryParse(tempString, out int temperatureMilliCelsius) && _provideSensors.Contains(tempFile))
                    {
                        double temperatureCelsius = temperatureMilliCelsius / 1000.0;
                        _sensorData[0].Item1 = tempFile;
                        _sensorData[0].Item2 = (float)Math.Round(temperatureCelsius, 1);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse temperature value from '{tempFile}': '{tempString}'.");
                    }
                }

                data = _sensorData;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading AMD CPU temperature data from '{_tempFilePathPattern}': {ex.Message}");
                data = Array.Empty<(string, float)>();
                return false;
            }
        }
    }
}