using System;
using System.Collections.Generic;

namespace HwMonLinux
{
    public class ExampleSensorDataProvider : ISensorDataProvider
    {
        private readonly Random _random = new Random();

        public string Name => "ExampleSensor";
        public string FriendlyName { get; }

        public ExampleSensorDataProvider(string friendlyName)
        { 
            FriendlyName = friendlyName;
        }

        public SensorData GetSensorData()
        {
            return new SensorData
            {
                Values = new Dictionary<string, object>
                {
                    { "temperature", _random.Next(20, 30) },
                    { "humidity", _random.Next(40, 60) }
                }
            };
        }
    }
}