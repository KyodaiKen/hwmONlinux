using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class LmsensorsSensorDataProvider : ISensorDataProvider
    {
        public string Name => "Lmsensors";
        private readonly string _filterRegex;
        private readonly Dictionary<string, string> _sensorNameOverrides;
        public string FriendlyName { get; }

        private SensorData _sensorData;

        /// <summary>
        /// Initializes a new instance of the <see cref="LmsensorsSensorDataProvider"/> class.
        /// </summary>
        /// <param name="friendlyName">A user-friendly name for this sensor group.</param>
        /// <param name="filterRegex">A regular expression to filter which sensors are included.
        /// If null or empty, all sensors will be included.</param>
        /// <param name="sensorNameOverrides">A dictionary to override raw sensor names with friendly names.</param>
        public LmsensorsSensorDataProvider(string friendlyName, string filterRegex = null, Dictionary<string, string> sensorNameOverrides = null)
        {
            FriendlyName = friendlyName;
            _filterRegex = string.IsNullOrEmpty(filterRegex) ? null : filterRegex;
            _sensorNameOverrides = sensorNameOverrides ?? new();
            _sensorData = new();
            _sensorData.Values = new();
        }

        public SensorData GetSensorData()
        {      
            try
            {
                string output;
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "sensors";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                }

                using (var reader = new StringReader(output))
                {
                    string line;
                    string currentChip = null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();

                        // Identify spd memory chip names (e.g., "spd5118-i2c-14-53")
                        if (Regex.IsMatch(line, @"^spd\d+-[a-zA-Z0-9]+-\d+-\d+$"))
                        {
                            currentChip = line;
                            continue;
                        }

                        // Identify other chip names (e.g., "acpitz-acpi-0")
                        if (Regex.IsMatch(line, @"^[a-zA-Z0-9]+-[a-zA-Z0-9]+-\d+$"))
                        {
                            currentChip = line;
                            continue;
                        }

                        // Identify sensor readings (e.g., "temp1:        +40.0°C  (crit = +90.0°C)")
                        Match sensorMatch = Regex.Match(line, @"^(.+?):\s+([+-]?\d+\.?\d*).+");
                        if (sensorMatch.Success)
                        {
                            string sensorNameRaw = sensorMatch.Groups[1].Value.Trim();
                            string sensorValueRaw = sensorMatch.Groups[2].Value.Trim();
                            string fullSensorNameRaw = currentChip != null ? $"{currentChip}-{sensorNameRaw}" : sensorNameRaw;
                            string finalSensorName = fullSensorNameRaw;

                            //Console.WriteLine(fullSensorNameRaw);
                            // Apply filter if one is provided
                            if (_filterRegex != null && !Regex.IsMatch(fullSensorNameRaw, _filterRegex))
                            {
                                continue;
                            }

                            // Apply sensor name overrides
                            if (_sensorNameOverrides.ContainsKey(fullSensorNameRaw))
                            {
                                finalSensorName = _sensorNameOverrides[fullSensorNameRaw];
                            }
                            else if (_sensorNameOverrides.ContainsKey(sensorNameRaw))
                            {
                                finalSensorName = _sensorNameOverrides[sensorNameRaw];
                            }

                            if (float.TryParse(sensorValueRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float sensorValue))
                            {
                                _sensorData.Values[finalSensorName] = sensorValue;
                            }
                            else if (sensorValueRaw.ToLowerInvariant() == "n/a")
                            {
                                _sensorData.Values[finalSensorName] = null; // Or some other indicator for N/A
                            }
                            // You might want to handle other units or states differently
                        }
                    }
                }

                return _sensorData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading LMSensors data: {ex.Message}");
                return null;
            }
        }
    }
}