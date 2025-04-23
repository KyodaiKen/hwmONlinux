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
        private readonly Dictionary<string, string> _sensorUnits = new Dictionary<string, string>
        {
            { "temperature.gpu", "°C" },
            { "temperature.memory", "°C" },
            { "utilization.gpu", "%" },
            { "utilization.memory", "%" },
            { "utilization.encoder", "%" },
            { "utilization.decoder", "%" },
            { "utilization.ofa", "%" },
            { "power.draw", "W" },
            { "power.draw.instant", "W" },
            { "clocks.current.graphics", "MHz" },
            { "clocks.current.sm", "MHz" },
            { "clocks.current.mem", "MHz" },
            { "clocks.current.memory", "MHz" },
            { "clocks.current.video", "MHz" },
            { "memory.total", "MB" },
            { "memory.free", "MB" },
            { "memory.used", "MB" },
            { "fan.speed", "%" }, // Assuming percentage if no explicit unit
            { "bar1.total", "MB" },
            { "bar1.free", "MB" },
            { "bar1.used", "MB" }
            // Add units for other relevant metrics if needed
        };

        // A list of all possible nvidia-smi query options (as of a certain point).
        // This list might need to be updated with newer driver versions.
        private static readonly List<string> AllNvidiaSmiMetrics = new List<string> {
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
            "gpu_util", // Alias for utilization.gpu
            "mem_util", // Alias for utilization.memory
            "encoder.util", // Alias for utilization.encoder
            "decoder.util", // Alias for utilization.decoder
            "ofa.util",
            "bar1.total",
            "bar1.free",
            "bar1.used"
            // Add other metrics as needed based on nvidia-smi --help-query-gpu
        };

        public NvidiaSmiSensorDataProvider(string friendlyName, List<string> queriedSensors = null, Dictionary<string, string> sensorNameOverrides = null)
        {
            FriendlyName = friendlyName;
            _queriedSensors = queriedSensors?.Where(s => AllNvidiaSmiMetrics.Contains(s)).ToList() ?? new List<string> {
                "name", "temperature.gpu", "utilization.gpu", "memory.used", "power.draw"
            };
            _sensorNameOverrides = sensorNameOverrides ?? new Dictionary<string, string>();
        }

        public SensorData GetSensorData()
        {
            var sensorValues = new Dictionary<string, object>();
            try
            {
                string queryArguments = $"--query-gpu={string.Join(",", _queriedSensors)} --format=csv,noheader,nounits";
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "nvidia-smi";
                    process.StartInfo.Arguments = queryArguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var lines = output.Trim().Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var values = lines[i].Split(',').Select(v => v.Trim()).ToList();
                        string gpuName = $"GPU{i}"; // Basic GPU naming

                        for (int j = 0; j < _queriedSensors.Count && j < values.Count; j++)
                        {
                            string rawSensorName = _queriedSensors[j];
                            string friendlySensorName = _sensorNameOverrides.ContainsKey(rawSensorName) ? _sensorNameOverrides[rawSensorName] : rawSensorName;
                            string fullSensorNameWithUnit = friendlySensorName;

                            if (_sensorUnits.TryGetValue(rawSensorName, out string unit))
                            {
                                fullSensorNameWithUnit += $" ({unit})";
                            }

                            if (float.TryParse(values[j], NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                            {
                                sensorValues[fullSensorNameWithUnit] = floatValue;
                            }
                            else if (int.TryParse(values[j], NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                            {
                                sensorValues[fullSensorNameWithUnit] = intValue;
                            }
                            else
                            {
                                sensorValues[fullSensorNameWithUnit] = values[j]; // Store as string if parsing fails
                            }
                        }
                    }
                }
                return new SensorData {
                    Values = sensorValues
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading NVIDIA SMI data: {ex.Message}");
                return null;
            }
        }
    }
}