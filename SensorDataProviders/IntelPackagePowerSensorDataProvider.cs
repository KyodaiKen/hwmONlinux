using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class IntelPackagePowerSensorDataProvider : ISensorDataProvider
    {
        public string Name => "cpu.power.intel.package";
        public string FriendlyName { get; }
        private readonly string _energyFilePath;

        private readonly List<string> _provideSensors;

        private ulong _previousEnergyMicroJoules = 0;
        private DateTime _previousReadTime = DateTime.MinValue;
        private (string, float)[] _sensorData;

        // Example energyFilePath = "/sys/class/powercap/intel-rapl/intel-rapl:0/energy_uj"
        public IntelPackagePowerSensorDataProvider(string friendlyName, string energyFilePath, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _energyFilePath = energyFilePath;
            _provideSensors = provideSensors;

            if (!File.Exists(_energyFilePath))
            {
                Console.WriteLine($"Warning: Energy statistics file not found at '{_energyFilePath}'. This provider might not function.");
            }

            _sensorData = new (string, float)[_provideSensors.Count];
        }

        public bool GetSensorData(out (string, float)[] data)
        {
            try
            {
                if (File.Exists(_energyFilePath))
                {
                    string energyString = File.ReadAllText(_energyFilePath).Trim();
                    if (ulong.TryParse(energyString, out ulong currentEnergyMicroJoules))
                    {
                        if (_previousReadTime != DateTime.MinValue)
                        {
                            DateTime currentTime = DateTime.UtcNow;
                            TimeSpan timeDiff = currentTime - _previousReadTime;
                            ulong energyDiff = currentEnergyMicroJoules - _previousEnergyMicroJoules;

                            if (timeDiff.TotalSeconds > 0)
                            {
                                // Convert microjoules to joules and divide by time in seconds to get Watts
                                _sensorData[0].Item1 = "last_second";
                                _sensorData[0].Item2 = (float)(energyDiff / timeDiff.TotalSeconds / 1_000_000.0);
                            }
                        }
                        _previousEnergyMicroJoules = currentEnergyMicroJoules;
                        _previousReadTime = DateTime.UtcNow;
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse energy value from '{_energyFilePath}': '{energyString}'.");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Energy statistics file not found at '{_energyFilePath}'.");
                }

                data = _sensorData;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading Intel package power data from '{_energyFilePath}': {ex.Message}");
                data = [];
                return false;
            }
        }
    }
}