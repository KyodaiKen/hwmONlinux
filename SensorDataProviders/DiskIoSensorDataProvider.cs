using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class DiskIoSensorDataProvider : ISensorDataProvider, IDisposable
    {
        public string Name => "DiskIO";
        public string FriendlyName { get; }
        private readonly Dictionary<string, string> _sensorNameOverrides;

        private Dictionary<string, (long ReadBytes, long WriteBytes, DateTime Timestamp)> _previousStats = new Dictionary<string, (long, long, DateTime)>();
        private List<string> _mountPoints = new List<string>();
        private SensorData _sensorData;
        private Dictionary<string, (long ReadBytes, long WriteBytes)> _currentStats;
        private bool _disposed = false;

        public DiskIoSensorDataProvider(string friendlyName, Dictionary<string, string> sensorNameOverrides = null)
        {
            FriendlyName = friendlyName;
            _sensorNameOverrides = sensorNameOverrides ?? new();
            _currentStats = new();
            _sensorData = new();
            _sensorData.Values = new();
            _mountPoints = GetMonitoredMountPoints();
        }

        private List<string> GetMonitoredMountPoints()
        {
            var mountPoints = new List<string>();
            try
            {
                var fstabContent = File.ReadAllLines("/etc/fstab");
                foreach (var line in fstabContent)
                {
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    {
                        var parts = line.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && !parts[1].Contains("/run/user/") && !parts[1].StartsWith("/media/")) // Exclude user mounts and media mounts
                        {
                            mountPoints.Add(parts[1]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading /etc/fstab: {ex.Message}");
            }
            return mountPoints;
        }

        public SensorData GetSensorData()
        {
            ReadDiskStats();
            var now = DateTime.UtcNow;

            foreach (var mountPoint in _mountPoints)
            {
                if (_currentStats.TryGetValue(mountPoint, out var current))
                {
                    // Apply sensor name overrides
                    string finalSensorName = mountPoint.Split('/')[2];
                    if (_sensorNameOverrides.ContainsKey(finalSensorName))
                    {
                        finalSensorName = _sensorNameOverrides[finalSensorName];
                    }
                    if (_previousStats.TryGetValue(mountPoint, out var previous))
                    {
                        var timeDiff = now - previous.Timestamp;
                        if (timeDiff.TotalSeconds > 0)
                        {
                            var readDiffBytes = current.ReadBytes - previous.ReadBytes;
                            var writeDiffBytes = current.WriteBytes - previous.WriteBytes;

                            var readMBps = readDiffBytes / timeDiff.TotalSeconds / (1024 * 1024d);
                            var writeMBps = writeDiffBytes / timeDiff.TotalSeconds / (1024 * 1024d);

                            _sensorData.Values[$"{finalSensorName} Read (MB/s)"] = Math.Round(readMBps, 3);
                            _sensorData.Values[$"{finalSensorName} Write (MB/s)"] = Math.Round(writeMBps, 3);
                        }
                    }
                    _previousStats[mountPoint] = (current.ReadBytes, current.WriteBytes, now);
                }
                else if (_previousStats.ContainsKey(mountPoint))
                {
                    _previousStats.Remove(mountPoint); // Disk might have been unmounted
                }
            }

            // Add stats for disks that appeared since last check
            foreach (var stat in _currentStats)
            {
                if (!_mountPoints.Contains(stat.Key) && !_previousStats.ContainsKey(stat.Key) && !stat.Key.Contains("/run/user/") && !stat.Key.StartsWith("/media/"))
                {
                    _mountPoints.Add(stat.Key);
                    _previousStats[stat.Key] = (stat.Value.ReadBytes, stat.Value.WriteBytes, now);
                }
            }

            return _sensorData;
        }

        private void ReadDiskStats()
        {
            try
            {
                var procDiskStats = File.ReadAllLines("/proc/diskstats");
                foreach (var line in procDiskStats)
                {
                    var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 14)
                    {
                        var deviceName = parts[2];
                        if (!deviceName.StartsWith("loop") && !deviceName.StartsWith("ram") && !deviceName.StartsWith("zram")) // Ignore loop and ram devices
                        {
                            long readSectors = long.Parse(parts[5]);
                            long writeSectors = long.Parse(parts[9]);
                            long sectorSize = 512; // Assuming 512 byte sectors (most common)
                            _currentStats[$"/dev/{deviceName}"] = (readSectors * sectorSize, writeSectors * sectorSize);
                        }
                    }
                }
                procDiskStats = [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading /proc/diskstats: {ex.Message}");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources if any
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~DiskIoSensorDataProvider()
        {
            Dispose(disposing: false);
        }
    }
}