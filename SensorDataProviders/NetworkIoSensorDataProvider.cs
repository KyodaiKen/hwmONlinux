using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HwMonLinux
{
        public class NetworkIoSensorDataProvider : ISensorDataProvider, IDisposable
    {
        public string Name => "NetworkIO";
        public string FriendlyName { get; }
        private readonly Dictionary<string, string> _sensorNameOverrides;
        private Dictionary<string, (long ReceivedBytes, long TransmittedBytes)> _networkStats;
        private SensorData _sensorData;

        private Dictionary<string, (long ReceivedBytes, long TransmittedBytes, DateTime Timestamp)> _previousStats = new Dictionary<string, (long, long, DateTime)>();
        private List<string> _networkInterfaces = new List<string>();
        private bool _disposed = false;

        public NetworkIoSensorDataProvider(string friendlyName,  Dictionary<string, string> sensorNameOverrides = null)
        {
            FriendlyName = friendlyName;
            _sensorNameOverrides = sensorNameOverrides ?? new Dictionary<string, string>();
            _networkInterfaces = GetActiveEthernetInterfaces();
            _sensorData = new();
            _sensorData.Values = new();
        }

        private List<string> GetActiveEthernetInterfaces()
        {
            var interfaces = new List<string>();
            try
            {
                var procNetDev = File.ReadAllLines("/proc/net/dev");
                foreach (var line in procNetDev.Skip(2)) // Skip header lines
                {
                    var parts = line.Split(new char[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1 && !parts[0].StartsWith("lo")) // Only consider ethernet interfaces
                    {
                        interfaces.Add(parts[0]);
                    }
                }
                procNetDev = [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading /proc/net/dev: {ex.Message}");
            }
            return interfaces;
        }

        public SensorData GetSensorData()
        {
            ReadNetworkStats();
            var now = DateTime.UtcNow;

            foreach (var iface in _networkInterfaces)
            {
                if (_networkStats.TryGetValue(iface, out var current))
                {
                    // Apply sensor name overrides
                    string finalSensorName = iface;
                    if (_sensorNameOverrides.ContainsKey(iface))
                    {
                        finalSensorName = _sensorNameOverrides[iface];
                    }
                    if (_previousStats.TryGetValue(iface, out var previous))
                    {
                        var timeDiff = now - previous.Timestamp;
                        if (timeDiff.TotalSeconds > 0)
                        {
                            var receivedDiffBytes = current.ReceivedBytes - previous.ReceivedBytes;
                            var transmittedDiffBytes = current.TransmittedBytes - previous.TransmittedBytes;

                            var receivedMbps = (double)receivedDiffBytes * 8 / timeDiff.TotalSeconds / (1000 * 1000); // Bytes to Bits, then to MBit/s
                            var transmittedMbps = (double)transmittedDiffBytes * 8 / timeDiff.TotalSeconds / (1000 * 1000); // Bytes to Bits, then to MBit/s

                            _sensorData.Values[$"{finalSensorName} Rx (MBit/s)"] = Math.Round(receivedMbps, 3);
                            _sensorData.Values[$"{finalSensorName} Tx (MBit/s)"] = Math.Round(transmittedMbps, 3);
                        }
                    }
                    _previousStats[iface] = (current.ReceivedBytes, current.TransmittedBytes, now);
                }
                else if (_previousStats.ContainsKey(iface))
                {
                    _previousStats.Remove(iface); // Interface might be down
                }
            }

            // Add stats for interfaces that became active since last check
            foreach (var stat in _networkStats)
            {
                if (!_networkInterfaces.Contains(stat.Key) && !_previousStats.ContainsKey(stat.Key) && !stat.Key.StartsWith("lo"))
                {
                    _networkInterfaces.Add(stat.Key);
                    _previousStats[stat.Key] = (stat.Value.ReceivedBytes, stat.Value.TransmittedBytes, now);
                }
            }
            return _sensorData;
        }

        private void ReadNetworkStats()
        {
            _networkStats ??= new();
            try
            {
                var procNetDev = File.ReadAllLines("/proc/net/dev");
                foreach (var line in procNetDev.Skip(2)) // Skip header lines
                {
                    var parts = line.Split(new char[] {':', ' '}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 9)
                    {
                        var interfaceName = parts[0];
                        long receivedBytes = long.Parse(parts[1]);
                        long transmittedBytes = long.Parse(parts[9]);
                        _networkStats[interfaceName] = (receivedBytes, transmittedBytes);
                    }
                }
                procNetDev = [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading /proc/net/dev: {ex.Message}");
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

        ~NetworkIoSensorDataProvider()
        {
            Dispose(disposing: false);
        }
    }
}