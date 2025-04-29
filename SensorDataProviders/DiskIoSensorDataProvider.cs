using System;
using System.Collections.Generic;
using System.IO;

namespace HwMonLinux
{
    public class DiskIoSensorDataProvider : ISensorDataProvider, IDisposable
    {
        public string Name => "io.disks";
        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;

        private Dictionary<string, (long ReadBytes, long WriteBytes, DateTime Timestamp)> _previousStats = new Dictionary<string, (long, long, DateTime)>();
        private List<string> _mountPoints = new List<string>();
        private (string, float)[] _sensorData;
        private Dictionary<string, (long ReadBytes, long WriteBytes)> _currentStats;
        private bool _disposed = false;

        public DiskIoSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _provideSensors = provideSensors;
            _currentStats = new();
            _mountPoints = GetMonitoredMountPoints();
            _sensorData = new (string, float)[_provideSensors.Count];
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

        public bool GetSensorData(out (string, float)[] data)
        {
            ReadDiskStats();
            var now = DateTime.UtcNow;

            int i = 0;
            foreach (var mountPoint in _mountPoints)
            {
                if (_currentStats.TryGetValue(mountPoint, out var current))
                {
                    // Apply sensor name overrides
                    string finalSensorName = mountPoint.Split('/')[2];
                    if (_previousStats.TryGetValue(mountPoint, out var previous))
                    {
                        var timeDiff = now - previous.Timestamp;
                        if (timeDiff.TotalSeconds > 0)
                        {
                            var readDiffBytes = current.ReadBytes - previous.ReadBytes;
                            var writeDiffBytes = current.WriteBytes - previous.WriteBytes;

                            var readMBps = readDiffBytes / timeDiff.TotalSeconds / (1024 * 1024d);
                            var writeMBps = writeDiffBytes / timeDiff.TotalSeconds / (1024 * 1024d);
                            
                            if (_provideSensors.Contains(finalSensorName+".r"))
                            {
                                _sensorData[i].Item1 = $"{finalSensorName}.r";
                                _sensorData[i].Item2 = (float)Math.Round(readMBps, 3);
                                i++;
                            }

                            if (_provideSensors.Contains(finalSensorName+".w"))
                            {
                                _sensorData[i].Item1 = $"{finalSensorName}.w";
                                _sensorData[i].Item2 = (float)Math.Round(writeMBps, 3);
                                i++;
                            }
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

            data = _sensorData;
            return true;
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
                procDiskStats = null;
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