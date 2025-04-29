using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class MemoryUtilizationSensorDataProvider : ISensorDataProvider
    {
        public string Name => "stats.memory";

        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;

        private (string, float)[] _sensorData;

        public MemoryUtilizationSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _provideSensors = provideSensors;
            _sensorData = new (string, float)[_provideSensors.Count];
        }

        public bool GetSensorData(out (string, float)[] data)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    string memInfo = File.ReadAllText("/proc/meminfo");
                    string[] lines = memInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    memInfo = "";

                    long totalMemoryKb = 0;
                    long freeMemoryKb = 0;
                    long buffersKb = 0;
                    long cachedKb = 0;
                    long usedMemoryKb = 0;
                    long swapTotalKb = 0;
                    long swapFreeKb = 0;
                    long usedSwapKb = 0;

                    int i = 0;
                    foreach (string line in lines)
                    {
                        string metric = line.Split(':')[0];

                        // Parse metric value
                        TryParseKbValue(line, out long value);
                        
                        if(_provideSensors.Contains(metric)) {
                            // Output
                            _sensorData[i].Item1 = metric;
                            _sensorData[i].Item2 = (float)Math.Round((double)value / (1024 * 1024), 2);
                            i++;
                        }

                        switch (metric)
                        {
                            case "MemTotal":
                                totalMemoryKb = value;
                                break;
                            case "MemFree":
                                freeMemoryKb = value;
                                break;
                            case "Buffers":
                                buffersKb = value;
                                break;
                            case "Cached":
                                cachedKb = value;
                                usedMemoryKb = totalMemoryKb - freeMemoryKb - buffersKb - cachedKb;
                                if (_provideSensors.Contains("MemUsed"))
                                {
                                    _sensorData[i].Item1 = "MemUsed";
                                    _sensorData[i].Item2 = (float)Math.Round((double)usedMemoryKb / (1024 * 1024), 2);
                                    i++;
                                }
                                if (_provideSensors.Contains("MemUtil%"))
                                {
                                    _sensorData[i].Item1 = "MemUtil%";
                                    _sensorData[i].Item2 = (float)Math.Round((double)usedMemoryKb / totalMemoryKb * 100, 2);
                                    i++;
                                }
                                break;
                            case "SwapTotal":
                                swapTotalKb = value;
                                break;
                            case "SwapFree":
                                swapFreeKb = value;
                                usedSwapKb = swapTotalKb - swapFreeKb;
                                if (_provideSensors.Contains("SwapUsed"))
                                {
                                    _sensorData[i].Item1 = "SwapUsed";
                                    _sensorData[i].Item2 = (float)Math.Round((double)usedSwapKb / (1024 * 1024), 2);
                                    i++;
                                }
                                if (_provideSensors.Contains("SwapUtil%"))
                                {
                                    _sensorData[i].Item1 = "SwapUtil%";
                                    _sensorData[i].Item2 = (float)Math.Round((double)usedSwapKb / swapTotalKb * 100, 2);
                                    i++;
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading memory information: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Memory utilization on non-Unix systems requires platform-specific implementation.");
                data = [];
                return false;
            }

            data = _sensorData;
            return true;
        }

        private static bool TryParseKbValue(string line, out long valueKb)
        {
            var match = Regex.Match(line, @"^[^:]+:\s+(\d+)\s+kB", RegexOptions.IgnoreCase);
            if (match.Success && long.TryParse(match.Groups[1].Value, out valueKb))
            {
                return true;
            }
            valueKb = 0;
            return false;
        }
    }
}