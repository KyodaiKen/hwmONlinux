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

        private Dictionary<string, (long ReceivedBytes, long TransmittedBytes, DateTime Timestamp)> _previousStats = new Dictionary<string, (long, long, DateTime)>();
        private List<string> _networkInterfaces = new List<string>();
        private bool _disposed = false;

        public NetworkIoSensorDataProvider(string friendlyName)
        {
            FriendlyName = friendlyName;
            _networkInterfaces = GetActiveEthernetInterfaces();
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading /proc/net/dev: {ex.Message}");
            }
            return interfaces;
        }

        public SensorData GetSensorData()
        {
            var currentStats = ReadNetworkStats();
            var sensorValues = new Dictionary<string, object>();
            var now = DateTime.UtcNow;

            foreach (var iface in _networkInterfaces)
            {
                if (currentStats.TryGetValue(iface, out var current))
                {

                    if (_previousStats.TryGetValue(iface, out var previous))
                    {
                        var timeDiff = now - previous.Timestamp;
                        if (timeDiff.TotalSeconds > 0)
                        {
                            var receivedDiffBytes = current.ReceivedBytes - previous.ReceivedBytes;
                            var transmittedDiffBytes = current.TransmittedBytes - previous.TransmittedBytes;

                            var receivedMbps = (double)receivedDiffBytes * 8 / timeDiff.TotalSeconds / (1000 * 1000); // Bytes to Bits, then to MBit/s
                            var transmittedMbps = (double)transmittedDiffBytes * 8 / timeDiff.TotalSeconds / (1000 * 1000); // Bytes to Bits, then to MBit/s

                            sensorValues[$"{iface} Rx (MBit/s)"] = Math.Round(receivedMbps, 3);
                            sensorValues[$"{iface} Tx (MBit/s)"] = Math.Round(transmittedMbps, 3);
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
            foreach (var stat in currentStats)
            {
                if (!_networkInterfaces.Contains(stat.Key) && !_previousStats.ContainsKey(stat.Key) && stat.Key.StartsWith("eth"))
                {
                    _networkInterfaces.Add(stat.Key);
                    _previousStats[stat.Key] = (stat.Value.ReceivedBytes, stat.Value.TransmittedBytes, now);
                }
            }
            return new SensorData { Values = sensorValues };
        }

        private Dictionary<string, (long ReceivedBytes, long TransmittedBytes)> ReadNetworkStats()
        {
            var networkStats = new Dictionary<string, (long, long)>();
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
                        networkStats[interfaceName] = (receivedBytes, transmittedBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading /proc/net/dev: {ex.Message}");
            }
            return networkStats;
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