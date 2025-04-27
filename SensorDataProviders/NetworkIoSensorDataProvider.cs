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
        public string Name => "io.network";
        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;
        
        private Dictionary<string, (long ReceivedBytes, long TransmittedBytes)> _networkStats;
        private (string, float)[] _sensorData;

        private Dictionary<string, (long ReceivedBytes, long TransmittedBytes, DateTime Timestamp)> _previousStats = new Dictionary<string, (long, long, DateTime)>();
        private List<string> _networkInterfaces = new List<string>();
        private bool _disposed = false;

        public NetworkIoSensorDataProvider(string friendlyName, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _provideSensors = provideSensors;
            _networkInterfaces = GetActiveEthernetInterfaces();
            _sensorData = new (string, float)[_provideSensors.Count * 2];
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

        public bool GetSensorData(out (string, float)[] data)
        {
            ReadNetworkStats();
            var now = DateTime.UtcNow;

            int i = 0;
            foreach (var iface in _networkInterfaces)
            {
                if (_networkStats.TryGetValue(iface, out var current))
                {
                    if (_previousStats.TryGetValue(iface, out var previous))
                    {
                        var timeDiff = now - previous.Timestamp;
                        if (timeDiff.TotalSeconds > 0)
                        {
                            if (_provideSensors.Contains(iface+".rx"))
                            {
                                var receivedDiffBytes = current.ReceivedBytes - previous.ReceivedBytes;
                                var receivedMbps = (double)receivedDiffBytes * 8 / timeDiff.TotalSeconds / (1000 * 1000); // Bytes to Bits, then to MBit/s
                                _sensorData[i].Item1 = iface+".rx";
                                _sensorData[i].Item2 = (float)Math.Round(receivedMbps, 3);
                                i++;
                            }

                            if (_provideSensors.Contains(iface+".tx"))
                            {
                                var transmittedDiffBytes = current.TransmittedBytes - previous.TransmittedBytes;
                                var transmittedMbps = (double)transmittedDiffBytes * 8 / timeDiff.TotalSeconds / (1000 * 1000); // Bytes to Bits, then to MBit/s
                                _sensorData[i].Item1 = iface+".tx";
                                _sensorData[i].Item2 = (float)Math.Round(transmittedMbps, 3);
                                i++;
                            }
                        }
                    }
                    _previousStats[iface] = (current.ReceivedBytes, current.TransmittedBytes, now);
                }
            }

            data = _sensorData;
            return true;
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