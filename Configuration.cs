using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HwMonLinux
{
    [YamlStaticContext]
    public class SensorProviderDefinition : StaticContext
    {
        public required string Type { get; set; }
        public required Dictionary<string, object> Config { get; set; }
    }

    [YamlStaticContext]
    public class SensorGroupDefinition : StaticContext
    {
        public required string Name { get; set; }
        public required string FriendlyName { get; set; }
        public required List<string> SensorIdentifiers { get; set; }
    }

    [YamlStaticContext]
    public class WebServerConfig : StaticContext
    {
        public required string Host { get; set; }
        public required int Port { get; set; }
        public required string ContentRoot { get; set; }
    }

    [YamlStaticContext]
    public class SensorDataConfig : StaticContext
    {
        public required int DataRetentionSeconds { get; set; }
    }

    [YamlStaticContext]
    [YamlSerializable(typeof(Configuration))]
    [YamlSerializable(typeof(SensorProviderDefinition))]
    [YamlSerializable(typeof(SensorGroupDefinition))]
    [YamlSerializable(typeof(WebServerConfig))]
    [YamlSerializable(typeof(SensorDataConfig))]
    public partial class YamlStaticContext : StaticContext { }

    public partial class Configuration
    {
        public required WebServerConfig WebServer { get; set; }
        public required SensorDataConfig SensorData { get; set; }
        public required List<SensorProviderDefinition> SensorProviders { get; set; }
        public required List<SensorGroupDefinition> SensorGroups { get; set; }

        public static Configuration Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Configuration file not found at: {path}");
            }

            var deserializer = new StaticDeserializerBuilder(new YamlStaticContext())
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            using (var reader = new StreamReader(path))
            {
                return deserializer.Deserialize<Configuration>(reader);
            }
        }
    }
}