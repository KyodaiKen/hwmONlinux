using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Specialized;

namespace HwMonLinux
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string configPath = "/etc/hwmONlinux/config.yaml";
            Configuration config = Configuration.Load(configPath);

            Console.WriteLine($"Starting webserver on http://{config.WebServer.Host}:{config.WebServer.Port}");
            Console.WriteLine($"{config.SensorData.DataRetentionSeconds} seconds of sensor data will be persistent.");

            // Create a folder for the static web server files
            Directory.CreateDirectory(config.WebServer.ContentRoot);

            // Create the sensor data store
            var sensorDataStore = new InMemorySensorDataStore(config.SensorData.DataRetentionSeconds);

            // Create a list of sensor data providers by loading from config
            var sensorDataProviders = new List<ISensorDataProvider>();
            foreach (var providerDef in config.SensorProviders)
            {
                ISensorDataProvider provider = LoadSensorProvider(providerDef);
                if (provider != null)
                {
                    sensorDataProviders.Add(provider);
                }
            }

            // Create a dictionary to hold grouped sensor data
            /*var groupedSensorData = new Dictionary<string, OrderedDictionary>();
            foreach (var groupDef in config.SensorGroups)
            {
                groupedSensorData[groupDef.Name] = new OrderedDictionary();
            }*/

            // Create and start web server
            var webServer = new WebServer(config.WebServer.Host, config.WebServer.Port, config.WebServer.ContentRoot, sensorDataStore, sensorDataProviders, new Dictionary<string, OrderedDictionary>(), config.SensorGroups); // Pass grouped data and group definitions
            await webServer.StartAsync();

            Console.WriteLine("Press any key to stop the server.");
            Console.ReadLine();

            await webServer.StopAsync();
        }

        static ISensorDataProvider LoadSensorProvider(SensorProviderDefinition providerDef)
        {
            string typeName = providerDef.Type;
            var providerConfig = providerDef.Config;
            Type type = Type.GetType(typeName);

            if (type != null && typeof(ISensorDataProvider).IsAssignableFrom(type))
            {
                var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (constructor != null)
                {
                    var parameters = constructor.GetParameters();
                    var constructorArgs = new List<object>();
                    bool canCreate = true;

                    foreach (var paramInfo in parameters)
                    {
                        if (providerConfig.TryGetValue(paramInfo.Name, out var configValue))
                        {
                            try
                            {
                                if (paramInfo.ParameterType == typeof(Dictionary<string, string>))
                                {
                                    constructorArgs.Add(ConvertToObjectDictionary<string, string>(configValue));
                                }
                                else if (paramInfo.ParameterType == typeof(List<string>))
                                {
                                    constructorArgs.Add(ConvertToStringList(configValue));
                                }
                                else
                                {
                                    constructorArgs.Add(Convert.ChangeType(configValue, paramInfo.ParameterType));
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not convert config value '{configValue}' for parameter '{paramInfo.Name}' of '{typeName}'. Skipping provider.");
                                canCreate = false;
                                break;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Configuration key '{paramInfo.Name}' not found for provider '{typeName}'. Skipping provider.");
                                canCreate = false;
                                break;
                        }
                    }

                    if (canCreate)
                    {
                        var instance = Activator.CreateInstance(type, constructorArgs.ToArray()) as ISensorDataProvider;
                        if (instance != null)
                        {
                            Console.WriteLine($"Loaded sensor provider: {instance.FriendlyName} ({instance.Name})");
                            return instance;
                        }
                        else
                        {
                            Console.WriteLine($"Error: Could not create instance of provider '{typeName}'.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Error: No suitable constructor found for provider '{typeName}'.");
                }
            }
            else
            {
                Console.WriteLine($"Error: Type '{typeName}' not found or does not implement ISensorDataProvider.");
            }
            return null;
        }

        static Dictionary<TKey, TValue> ConvertToObjectDictionary<TKey, TValue>(object obj)
        {
            if (obj is Dictionary<object, object> rawDict)
            {
                var result = new Dictionary<TKey, TValue>();
                foreach (var entry in rawDict)
                {
                    if (entry.Key is TKey key && entry.Value is TValue value)
                    {
                        result[key] = value;
                    }
                }
                return result;
            }
            return null;
        }

        static List<string> ConvertToStringList(object obj)
        {
            if (obj is List<object> rawList)
            {
                return rawList.Select(item => item?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
            return null;
        }
    }
}