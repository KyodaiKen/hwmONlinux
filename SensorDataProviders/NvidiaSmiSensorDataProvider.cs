using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace HwMonLinux
{
    public class NvidiaSmiSensorDataProvider : ISensorDataProvider
    {
        public string Name => "NvidiaSmi";
        public string FriendlyName { get; }
        private readonly List<string> _queriedSensors;
        private readonly Dictionary<string, string> _sensorNameOverrides;

        private SensorData _sensorData;

        // A list of all possible nvidia-smi query options (as of a certain point).
        // This list might need to be updated with newer driver versions.
        private static readonly List<string> AllNvidiaSmiMetrics = [
            "timestamp",
            "name",
            "pci.bus_id",
            "driver_version",
            "cuda_version",
            "compute_mode",
            "pstate",
            "clocks.current.graphics",
            "clocks.current.sm",
            "clocks.current.mem",
            "clocks.current.video",
            "clocks.max.graphics",
            "clocks.max.sm",
            "clocks.max.mem",
            "clocks.max.video",
            "clocks.current.memory",
            "power.management",
            "power.draw",
            "power.draw.instant",
            "power.limit",
            "power.default_limit",
            "enforced.power.limit",
            "temperature.gpu",
            "temperature.memory",
            "temperature.gpu.threshold.slowdown",
            "temperature.gpu.threshold.shutdown",
            "utilization.gpu",
            "utilization.memory",
            "utilization.encoder",
            "utilization.decoder",
            "utilization.ofa",
            "ecc.mode.current",
            "ecc.errors.corrected.total",
            "ecc.errors.uncorrected.total",
            "fan.speed",
            "fan.pwm",
            "memory.total",
            "memory.free",
            "memory.used",
            "memory.reserved",
            "mig.mode.current",
            "mig.devices.count",
            "vbios_version",
            "serial",
            "uuid",
            "inforom.oem.tag",
            "inforom.ecc.object",
            "inforom.pwr_mgmt.object",
            "display.mode",
            "display.active",
            "persistence_mode",
            "gpu_virtualization_mode",
            "total_cuda_cores",
            "ofa.util",
            "bar1.total",
            "bar1.free",
            "bar1.used"
            // Add other metrics as needed based on nvidia-smi --help-query-gpu
        ];

        public NvidiaSmiSensorDataProvider(string friendlyName, List<string> queriedSensors = null, Dictionary<string, string> sensorNameOverrides = null)
        {
            FriendlyName = friendlyName;
            _queriedSensors = queriedSensors?.Where(s => AllNvidiaSmiMetrics.Contains(s)).ToList() ?? new List<string> {
                "name", "temperature.gpu", "utilization.gpu", "memory.used", "power.draw"
            };
            _sensorNameOverrides = sensorNameOverrides ?? new();
            _sensorData ??= new();
            _sensorData.Values ??= new();
        }

        public SensorData GetSensorData()
        {
            try
            {
                string queryArguments = $"--query-gpu={string.Join(",", _queriedSensors)} --format=csv,noheader,nounits";
                string output;
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "nvidia-smi";
                    process.StartInfo.Arguments = queryArguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                }

                var lines = output.Trim().Split('\n');
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var values = lines[i].Split(',').Select(v => v.Trim()).ToList();
                    for (int j = 0; j < _queriedSensors.Count && j < values.Count; j++)
                    {
                        string rawSensorName = _queriedSensors[j];
                        string friendlySensorName = _sensorNameOverrides.ContainsKey(rawSensorName) ? _sensorNameOverrides[rawSensorName] : rawSensorName;
                        string fullSensorNameWithUnit = friendlySensorName;

                        if (float.TryParse(values[j], NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                        {
                            _sensorData.Values[fullSensorNameWithUnit] = floatValue;
                        }
                        else if (int.TryParse(values[j], NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                        {
                            _sensorData.Values[fullSensorNameWithUnit] = intValue;
                        }
                        else
                        {
                            _sensorData.Values[fullSensorNameWithUnit] = values[j];
                        }
                    }
                    values = [];
                }
                return _sensorData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading NVIDIA SMI data: {ex.Message}");
                return null;
            }
        }
    }
}