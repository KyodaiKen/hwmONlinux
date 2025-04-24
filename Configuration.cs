using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HwMonLinux
{
    public class SensorProviderDefinition
    {
        public string Type { get; set; }
        public Dictionary<object, object> Config { get; set; }
        public List<string> PublishedSensors { get; set; } // Optional: List of specific sensors to publish
    }

    public class SensorGroupDefinition
    {
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public List<string> SensorIdentifiers { get; set; } // Identifiers to match sensors from providers
    }

    public class WebServerConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string ContentRoot { get; set; }
    }

    public class SensorDataConfig
    {
        public int DataRetentionSeconds { get; set; }
    }

    public class Configuration
    {
        public WebServerConfig WebServer { get; set; }
        public SensorDataConfig SensorData { get; set; }
        public List<SensorProviderDefinition> SensorProviders { get; set; }
        public List<SensorGroupDefinition> SensorGroups { get; set; } // New property for sensor groups

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