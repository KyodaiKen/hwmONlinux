using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HwMonLinux
{
    public class Configuration
    {
        public WebServerSettings WebServer { get; set; } = new WebServerSettings();
        public SensorDataSettings SensorData { get; set; } = new SensorDataSettings();
        public List<ProviderDefinition> SensorProviders { get; set; } = new List<ProviderDefinition>();

        public class WebServerSettings
        {
            public string Host { get; set; } = "0.0.0.0";
            public int Port { get; set; } = 8080;
            public string ContentRoot { get; set; } = "wwwroot"; // Directory for static files
        }

        public class SensorDataSettings
        {
            public int DataRetentionSeconds { get; set; } = 120;
        }

        public class ProviderDefinition
        {
            public string Type { get; set; }
            public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>();
        }

        public static Configuration Load(string configPath)
        {
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Cannot find configuration file: {configPath}. Using defaults.");
                return new Configuration();
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            try
            {
                var yaml = File.ReadAllText(configPath);
                return deserializer.Deserialize<Configuration>(yaml);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while reading the configuration file: {ex.Message}. Using defaults.");
                return new Configuration();
            }
        }
    }
}