using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HwMonLinux
{
    public class SensorData
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, float> Values { get; set; } = [];
    }
}