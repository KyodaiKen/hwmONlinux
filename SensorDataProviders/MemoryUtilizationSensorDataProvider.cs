using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class MemoryUtilizationSensorDataProvider : ISensorDataProvider
    {
        public string FriendlyName { get; }

        public string Name => "memory";

        public MemoryUtilizationSensorDataProvider(string friendlyName)
        {
            FriendlyName = friendlyName;
        }

        public SensorData GetSensorData()
        {
            var data = new SensorData { Values = new Dictionary<string, object>() };

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    string memInfo = File.ReadAllText("/proc/meminfo");
                    string[] lines = memInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    long totalMemoryKb = 0;
                    long freeMemoryKb = 0;
                    long availableMemoryKb = 0;
                    long buffersKb = 0;
                    long cachedKb = 0;
                    long swapTotalKb = 0;
                    long swapFreeKb = 0;

                    foreach (string line in lines)
                    {
                        if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out totalMemoryKb)) ;
                        }
                        else if (line.StartsWith("MemFree:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out freeMemoryKb)) ;
                        }
                        else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out availableMemoryKb)) ;
                        }
                        else if (line.StartsWith("Buffers:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out buffersKb)) ;
                        }
                        else if (line.StartsWith("Cached:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out cachedKb)) ;
                        }
                        else if (line.StartsWith("SwapTotal:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out swapTotalKb)) ;
                        }
                        else if (line.StartsWith("SwapFree:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryParseKbValue(line, out swapFreeKb)) ;
                        }
                    }

                    // Calculate memory utilization
                    if (totalMemoryKb > 0)
                    {
                        long usedMemoryKb = totalMemoryKb - freeMemoryKb - buffersKb - cachedKb;
                        double memoryUsage = (double)usedMemoryKb / totalMemoryKb;
                        data.Values["Memory Utilization (%)"] = Math.Round(memoryUsage * 100, 2);
                        data.Values["Memory Total (GB)"] = Math.Round((double)totalMemoryKb / (1024 * 1024), 2);
                        data.Values["Memory Used (GB)"] = Math.Round((double)usedMemoryKb / (1024 * 1024), 2);
                        data.Values["Memory Free (GB)"] = Math.Round((double)(freeMemoryKb + buffersKb + cachedKb) / (1024 * 1024), 2);
                    }

                    // Calculate swap utilization
                    if (swapTotalKb > 0)
                    {
                        long usedSwapKb = swapTotalKb - swapFreeKb;
                        double swapUsage = (double)usedSwapKb / swapTotalKb;
                        data.Values["Swap Utilization (%)"] = Math.Round(swapUsage * 100, 2);
                        data.Values["Swap Total (GB)"] = Math.Round((double)swapTotalKb / (1024 * 1024), 2);
                        data.Values["Swap Used (GB)"] = Math.Round((double)usedSwapKb / (1024 * 1024), 2);
                        data.Values["Swap Free (GB)"] = Math.Round((double)swapFreeKb / (1024 * 1024), 2);
                    }
                    else
                    {
                        data.Values["Swap Utilization (%)"] = 0.0;
                        data.Values["Swap Total (GB)"] = 0.0;
                        data.Values["Swap Used (GB)"] = 0.0;
                        data.Values["Swap Free (GB)"] = 0.0;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading memory information: {ex.Message}");
                }
            }
            else
            {
                data.Values["Memory Utilization (%)"] = 0.0;
                data.Values["Memory Total (GB)"] = 0.0;
                data.Values["Memory Used (GB)"] = 0.0;
                data.Values["Memory Free (GB)"] = 0.0;
                data.Values["Swap Utilization (%)"] = 0.0;
                data.Values["Swap Total (GB)"] = 0.0;
                data.Values["Swap Used (GB)"] = 0.0;
                data.Values["Swap Free (GB)"] = 0.0;
                Console.WriteLine("Memory utilization on non-Unix systems requires platform-specific implementation.");
            }

            return data;
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