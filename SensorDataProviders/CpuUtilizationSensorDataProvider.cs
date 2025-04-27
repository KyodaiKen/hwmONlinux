using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HwMonLinux
{
    public class CpuUtilizationSensorDataProvider : ISensorDataProvider
    {
        public string Name => "cpu.util";
        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;

        private DateTime _lastSampleTimeOverall = DateTime.MinValue;
        private TimeSpan _lastIdleTimeOverall = TimeSpan.Zero;
        private TimeSpan _lastTotalTimeOverall = TimeSpan.Zero;
        private readonly Dictionary<string, (DateTime lastTime, long lastUser, long lastNice, long lastSystem, long lastIdle, long lastIowait, long lastIrq, long lastSoftirq, long lastSteal)> _coreStats = new Dictionary<string, (DateTime, long, long, long, long, long, long, long, long)>();
        private (string, float)[] _sensorData;


        public CpuUtilizationSensorDataProvider(string friendlyName, List<string> provideSensors)
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
                    string cpuStat = File.ReadAllText("/proc/stat");
                    string[] lines = cpuStat.Split('\n');
                    cpuStat = "";
                    DateTime currentTime = DateTime.UtcNow;

                    int i = 0; 
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("cpu ", StringComparison.OrdinalIgnoreCase) && _provideSensors.Contains("overall"))
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
                                    string finalSensorName = "overall";

                                    if (totalDifference.TotalMilliseconds > 0)
                                    {
                                        double cpuUsage = 1.0 - (idleDifference.TotalMilliseconds / totalDifference.TotalMilliseconds);
                                        _sensorData[i].Item1 = finalSensorName;
                                        _sensorData[i].Item2 = (float)Math.Round(cpuUsage * 100, 2);
                                    }
                                    else
                                    {
                                        _sensorData[i].Item1 = finalSensorName;
                                        _sensorData[i].Item2 = 0f;
                                    }
                                    i++;
                                }

                                _lastSampleTimeOverall = currentTime;
                                _lastIdleTimeOverall = currentIdleTime;
                                _lastTotalTimeOverall = currentTotalTime;
                            }
                        }
                        
                        if (line.StartsWith("cpu", StringComparison.OrdinalIgnoreCase) && line.Length > 3 && char.IsDigit(line[3]))
                        {
                            string coreId = line.Substring(3).Trim();
                            if (_provideSensors.Contains(coreId))
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

                                    if (_coreStats.TryGetValue(coreId, out var lastStats))
                                    {
                                        TimeSpan timeDiff = currentTime - lastStats.lastTime;
                                        long idleDiff = idle + iowait - lastStats.lastIdle - lastStats.lastIowait;
                                        long totalDiff = user + nice + system + idle + iowait + irq + softirq + steal -
                                                        (lastStats.lastUser + lastStats.lastNice + lastStats.lastSystem + lastStats.lastIdle + lastStats.lastIowait + lastStats.lastIrq + lastStats.lastSoftirq + lastStats.lastSteal);

                                        if (timeDiff.TotalMilliseconds > 0 && totalDiff > 0)
                                        {

                                            double coreUsage = 1.0 - (double)idleDiff / totalDiff;
                                            _sensorData[i].Item1 = coreId;
                                            _sensorData[i].Item2 = (float)Math.Round(coreUsage * 100, 2);
                                        }
                                        else
                                        {
                                            _sensorData[i].Item1 = coreId;
                                            _sensorData[i].Item2 = 0f;
                                        }
                                        i++;
                                    }

                                    _coreStats[coreId] = (currentTime, user, nice, system, idle, iowait, irq, softirq, steal);
                                }
                            }
                        }
                    }
                    lines = [];
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading CPU utilization: {ex.Message}");
                }
            }
            else
            {
                data = [];
                Console.WriteLine("CPU Utilization on non-Unix systems requires platform-specific implementation for accurate historical comparison.");
                return false;
            }

            data = _sensorData;
            return true;
        }
    }
}