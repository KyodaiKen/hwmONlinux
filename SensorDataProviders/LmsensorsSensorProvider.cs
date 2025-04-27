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
        public string Name => "lmsensors";
        private readonly List<string> _provideSensors;
        public string FriendlyName { get; }

        private (string, float)[] _sensorData;

        public LmsensorsSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName ?? "";
            _provideSensors = provideSensors;
            _sensorData = new (string, float)[provideSensors.Count];
        }

        public bool GetSensorData(out (string, float)[] data)
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

                    int i = 0;
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

                            if (float.TryParse(sensorValueRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float sensorValue))
                            {
                                if (_provideSensors.Contains(fullSensorNameRaw))
                                {
                                    _sensorData[i].Item1 = fullSensorNameRaw;
                                    _sensorData[i].Item2 = sensorValue;
                                    i++;
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
                Console.WriteLine($"Error reading LMSensors data: {ex.Message}");
                data = [];
                return false;
            }
        }
    }
}