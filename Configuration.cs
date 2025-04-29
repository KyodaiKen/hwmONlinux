using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HwMonLinux;

[YamlSerializable(typeof(SensorProviderDefinition))]
public class SensorProviderDefinition
{
    public required string Type { get; set; }
    public required Dictionary<string, object> Config { get; set; }
}

[YamlSerializable(typeof(SensorGroupDefinition))]
public class SensorGroupDefinition
{
    public required string Name { get; set; }
    public required string FriendlyName { get; set; }
    public required List<string> SensorIdentifiers { get; set; }
}

[YamlSerializable(typeof(WebServerConfig))]
public class WebServerConfig
{
    public required string Host { get; set; }
    public required int Port { get; set; }
    public required string ContentRoot { get; set; }
}

[YamlSerializable(typeof(SensorDataConfig))]
public class SensorDataConfig
{
    public required int DataRetentionSeconds { get; set; }
}

[YamlSerializable(typeof(Configuration))]
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

        using (var reader = new StreamReader(path))
        {
            return YamlStaticContext.Deserializer.Deserialize<Configuration>(reader);
        }
    }
}


[YamlStaticContext]
public partial class YamlStaticContext : StaticContext
{
    public static readonly IDeserializer Deserializer =
        new StaticDeserializerBuilder(new YamlStaticContext())
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
}