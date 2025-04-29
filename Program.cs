using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

            // Create a list of sensor data providers and the sensor index
            var sensorDataProviders = new List<ISensorDataProvider>();
            var sensorIndexBuilder = new List<(string, (string, string)[])>();
            var allSensorLabels = new Dictionary<string, Dictionary<string, string>>(); // Store labels per provider

            if (config.SensorProviders != null)
            {
                foreach (var providerDef in config.SensorProviders)
                {
                    ISensorDataProvider provider = LoadSensorProvider(providerDef, out (string, string)[] providedSensors, out Dictionary<string, string> labels);
                    if (provider != null && providedSensors != null && providedSensors.Length > 0)
                    {
                        sensorDataProviders.Add(provider);
                        sensorIndexBuilder.Add((provider.Name, providedSensors));
                        allSensorLabels[provider.Name] = labels;
                    }
                    else if (provider != null)
                    {
                        Console.WriteLine($"Warning: Sensor provider '{provider.Name}' loaded, but no 'provideSensors' configured or found.");
                    }
                }
            }

            // Create and start web server
            var webServer = new WebServer(
                config.WebServer.Host,
                config.WebServer.Port,
                config.WebServer.ContentRoot,
                config.SensorData.DataRetentionSeconds,
                sensorDataProviders,
                config.SensorGroups,
                sensorIndexBuilder.ToArray(), // Pass the sensor index
                allSensorLabels // Pass the sensor labels
            );
            await webServer.StartAsync();

            Console.WriteLine("Press any key to stop the server.");
            Console.ReadLine();

            await webServer.StopAsync();
        }

        static ISensorDataProvider LoadSensorProvider(SensorProviderDefinition providerDef, out (string, string)[] providedSensors, out Dictionary<string, string> labels)
        {
            string typeName = providerDef.Type;
            var providerConfig = providerDef.Config;
            Type type = Type.GetType(typeName);
            providedSensors = null;
            labels = new Dictionary<string, string>();

            if (providerConfig.TryGetValue("sensorLabels", out var sensorLabelsObj) && sensorLabelsObj is Dictionary<object, object> rawLabels)
            {
                foreach (var entry in rawLabels)
                    {
                        if (entry.Key != null)
                        {
                            labels[entry.Key.ToString()] = entry.Value?.ToString() ?? "";
                        }
                    }
            }

            // Capture the labels dictionary in a local variable
            var localLabels = labels;

            if (type != null && typeof(ISensorDataProvider).IsAssignableFrom(type))
            {
                var constructors = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length);

                foreach (var constructor in constructors)
                {
                    var parameters = constructor.GetParameters();
                    var constructorArgs = new List<object>();
                    bool canCreate = true;
                    List<string> foundProvidedSensorsList = null;

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
                                    var stringList = ConvertToStringList(configValue);
                                    constructorArgs.Add(stringList);
                                    if (paramInfo.Name == "provideSensors")
                                    {
                                        foundProvidedSensorsList = stringList;
                                    }
                                }
                                else if (paramInfo.ParameterType == typeof(string))
                                {
                                    constructorArgs.Add(configValue?.ToString());
                                }
                                else if (paramInfo.ParameterType == typeof(int))
                                {
                                    constructorArgs.Add(Convert.ToInt32(configValue));
                                }
                                else if (paramInfo.ParameterType == typeof(long))
                                {
                                    constructorArgs.Add(Convert.ToInt64(configValue));
                                }
                                else if (paramInfo.ParameterType == typeof(bool))
                                {
                                    constructorArgs.Add(Convert.ToBoolean(configValue));
                                }
                                else
                                {
                                    constructorArgs.Add(Convert.ChangeType(configValue, paramInfo.ParameterType));
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not convert config value '{configValue}' for parameter '{paramInfo.Name}' of '{typeName}'. Trying next constructor.");
                                canCreate = false;
                                break;
                            }
                        }
                        else if (!paramInfo.IsOptional)
                        {
                            Console.WriteLine($"Warning: Configuration key '{paramInfo.Name}' not found for non-optional parameter of provider '{typeName}'. Trying next constructor.");
                            canCreate = false;
                            break;
                        }
                        else
                        {
                            constructorArgs.Add(Type.Missing); // Use default value for optional parameter
                        }
                    }

                    if (canCreate)
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(type, constructorArgs.ToArray()) as ISensorDataProvider;
                            if (instance != null)
                            {
                                providedSensors = foundProvidedSensorsList?.Select(sensorName => (sensorName, localLabels.TryGetValue(sensorName, out var label) ? label : sensorName)).ToArray();
                                Console.WriteLine($"Loaded sensor provider: {instance.FriendlyName} ({instance.Name}) providing sensors: {string.Join(", ", providedSensors?.Select(p => $"{p.Item1} (Label: {p.Item2})") ?? new string[0])}");
                                return instance;
                            }
                            else
                            {
                                Console.WriteLine($"Error: Could not create instance of provider '{typeName}' using constructor: {constructor}.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error creating instance of provider '{typeName}' using constructor: {constructor}. Exception: {ex.Message}");
                        }
                        // If instance creation fails, we continue to the next constructor
                    }
                }

                Console.WriteLine($"Error: No suitable constructor found for provider '{typeName}' that could be satisfied with the provided configuration.");
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