using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

        // Initialize a new sensor data array for each read to avoid potential issues
        _sensorData = new (string, float)[_provideSensors.Count];
        // Initialize all elements to default values
        for (int k = 0; k < _sensorData.Length; k++)
        {
            _sensorData[k].Item1 = _provideSensors[k]; // Initialize with the expected name
            _sensorData[k].Item2 = float.NaN; // Initialize value to NaN or a default indicating no reading yet
        }

        using (var reader = new StringReader(output))
        {
            string line;
            string currentChip = "";

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                // Identify spd memory chip names
                if (Regex.IsMatch(line, @"^spd\d+-[a-zA-Z0-9]+-\d+-\d+$"))
                {
                    currentChip = line;
                    continue;
                }

                // Identify other chip names
                if (Regex.IsMatch(line, @"^[a-zA-Z0-9]+-[a-zA-Z0-9]+-\d+$"))
                {
                    currentChip = line;
                    continue;
                }

                // Identify sensor readings
                Match sensorMatch = Regex.Match(line, @"^(.+?):\s+([+-]?\d+\.?\d*).+");
                if (sensorMatch.Success)
                {
                    string sensorNameRaw = sensorMatch.Groups[1].Value.Trim();
                    string sensorValueRaw = sensorMatch.Groups[2].Value.Trim();
                    string fullSensorNameRaw = currentChip != null ? $"{currentChip}.{sensorNameRaw}" : sensorNameRaw;

                    int index = _provideSensors.IndexOf(fullSensorNameRaw);
                    if (index != -1)
                    {
                        if (float.TryParse(sensorValueRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float sensorValue))
                        {
                            _sensorData[index].Item2 = sensorValue;
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