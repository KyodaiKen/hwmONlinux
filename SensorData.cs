using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HwMonLinux
{
    public class SensorData
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>();
    }
}