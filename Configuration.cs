using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HwMonLinux
{
    public class SensorProviderDefinition
    {
        public required string Type { get; set; }
        public required Dictionary<object, object> Config { get; set; }
        public required List<string> PublishedSensors { get; set; } // Optional: List of specific sensors to publish
    }

    public class SensorGroupDefinition
    {
        public required string Name { get; set; }
        public required string FriendlyName { get; set; }
        public required List<string> SensorIdentifiers { get; set; } // Identifiers to match sensors from providers
    }

    public class WebServerConfig
    {
        public required string Host { get; set; }
        public required int Port { get; set; }
        public required string ContentRoot { get; set; }
    }

    public class SensorDataConfig
    {
        public required int DataRetentionSeconds { get; set; }
    }

    public class Configuration
    {
        public required WebServerConfig WebServer { get; set; }
        public required SensorDataConfig SensorData { get; set; }
        public required List<SensorProviderDefinition> SensorProviders { get; set; }
        public required List<SensorGroupDefinition> SensorGroups { get; set; } // New property for sensor groups

        public static Configuration Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Configuration file not found at: {path}");
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            using (var reader = new StreamReader(path))
            {
                return deserializer.Deserialize<Configuration>(reader);
            }
        }
    }
}