using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class CpuUtilizationSensorDataProvider : ISensorDataProvider
    {
        public string FriendlyName { get; }
        private DateTime _lastSampleTimeOverall = DateTime.MinValue;
        private TimeSpan _lastIdleTimeOverall = TimeSpan.Zero;
        private TimeSpan _lastTotalTimeOverall = TimeSpan.Zero;
        private readonly Dictionary<string, (DateTime lastTime, long lastUser, long lastNice, long lastSystem, long lastIdle, long lastIowait, long lastIrq, long lastSoftirq, long lastSteal)> _coreStats = new Dictionary<string, (DateTime, long, long, long, long, long, long, long, long)>();
        private SensorData _sensorData;

        public string Name => "cpu";

        public CpuUtilizationSensorDataProvider(string friendlyName)
        {
            FriendlyName = friendlyName;
        }

        public SensorData GetSensorData()
        {
            _sensorData ??= new();
            _sensorData.Values ??= new();

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    string cpuStat = File.ReadAllText("/proc/stat");
                    string[] lines = cpuStat.Split('\n');
                    DateTime currentTime = DateTime.UtcNow;

                    foreach (string line in lines)
                    {
                        if (line.StartsWith("cpu ", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = Regex.Split(line.Trim(), @"\s+");
                            if (parts.Length >= 5 && long.TryParse(parts[1], out long user) &&
                                long.TryParse(parts[2], out long nice) && long.TryParse(parts[3], out long system) &&
                                long.TryParse(parts[4], out long idle))
                            {
                                long iowait = parts.Length > 5 && long.TryParse(parts[5], out long io) ? io : 0;
                                long irq = parts.Length > 6 && long.TryParse(parts[6], out long irqVal) ? irqVal : 0;
                                long softirq = parts.Length > 7 && long.TryParse(parts[7], out long softIrqVal) ? softIrqVal : 0;
                                long steal = parts.Length > 8 && long.TryParse(parts[8], out long stealVal) ? stealVal : 0;

                                TimeSpan currentIdleTime = TimeSpan.FromTicks(idle + iowait);
                                TimeSpan currentTotalTime = TimeSpan.FromTicks(user + nice + system + idle + iowait + irq + softirq + steal);

                                if (_lastSampleTimeOverall != DateTime.MinValue)
                                {
                                    TimeSpan idleDifference = currentIdleTime - _lastIdleTimeOverall;
                                    TimeSpan totalDifference = currentTotalTime - _lastTotalTimeOverall;

                                    if (totalDifference.TotalMilliseconds > 0)
                                    {
                                        double cpuUsage = 1.0 - (idleDifference.TotalMilliseconds / totalDifference.TotalMilliseconds);
                                        _sensorData.Values["Overall Utilization (%)"] = Math.Round(cpuUsage * 100, 2);
                                    }
                                }

                                _lastSampleTimeOverall = currentTime;
                                _lastIdleTimeOverall = currentIdleTime;
                                _lastTotalTimeOverall = currentTotalTime;
                            }
                        }
                        else if (line.StartsWith("cpu", StringComparison.OrdinalIgnoreCase) && line.Length > 3 && char.IsDigit(line[3]))
                        {
                            string coreId = line.Substring(3).Trim();
                            string[] parts = Regex.Split(line.Trim(), @"\s+");
                            if (parts.Length >= 5 && long.TryParse(parts[1], out long user) &&
                                long.TryParse(parts[2], out long nice) && long.TryParse(parts[3], out long system) &&
                                long.TryParse(parts[4], out long idle))
                            {
                                long iowait = parts.Length > 5 && long.TryParse(parts[5], out long io) ? io : 0;
                                long irq = parts.Length > 6 && long.TryParse(parts[6], out long irqVal) ? irqVal : 0;
                                long softirq = parts.Length > 7 && long.TryParse(parts[7], out long softIrqVal) ? softIrqVal : 0;
                                long steal = parts.Length > 8 && long.TryParse(parts[8], out long stealVal) ? stealVal : 0;

                                if (_coreStats.TryGetValue(coreId, out var lastStats))
                                {
                                    TimeSpan timeDiff = currentTime - lastStats.lastTime;
                                    long idleDiff = idle + iowait - lastStats.lastIdle - lastStats.lastIowait;
                                    long totalDiff = user + nice + system + idle + iowait + irq + softirq + steal -
                                                       (lastStats.lastUser + lastStats.lastNice + lastStats.lastSystem + lastStats.lastIdle + lastStats.lastIowait + lastStats.lastIrq + lastStats.lastSoftirq + lastStats.lastSteal);

                                    if (timeDiff.TotalMilliseconds > 0 && totalDiff > 0)
                                    {
                                        double coreUsage = 1.0 - (double)idleDiff / totalDiff;
                                        _sensorData.Values[$"Core {coreId} Utilization (%)"] = Math.Round(coreUsage * 100, 2);
                                    }
                                }

                                _coreStats[coreId] = (currentTime, user, nice, system, idle, iowait, irq, softirq, steal);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading CPU utilization: {ex.Message}");
                }
            }
            else
            {
                _sensorData.Values["Overall Utilization (%)"] = 0.0;
                Console.WriteLine("CPU Utilization on non-Unix systems requires platform-specific implementation for accurate historical comparison.");
            }

            return _sensorData;
        }
    }
}