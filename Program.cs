using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

            // Create the sensor data store
            var sensorDataStore = new InMemorySensorDataStore(config.SensorData.DataRetentionSeconds);

            // Create a list of sensor data providers by loading from config
            var sensorDataProviders = new List<ISensorDataProvider>();
            foreach (var providerDef in config.SensorProviders)
            {
                string typeName = providerDef.Type;
                var providerConfig = providerDef.Config;
                Type type = Type.GetType(typeName);

                // ... load the type ...
                if (type != null && typeof(ISensorDataProvider).IsAssignableFrom(type))
                {
                    // Find the constructor that matches the config keys
                    var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

                    if (constructor != null)
                    {
                        var parameters = constructor.GetParameters();
                        var constructorArgs = new List<object>();
                        bool canCreate = true;

                        if (typeName == "HwMonLinux.LmsensorsSensorDataProvider, HwmONlinuxServer")
                        {
                            string friendlyName = providerConfig.TryGetValue("friendlyName", out var friendlyNameObj) ? friendlyNameObj?.ToString() : null;
                            string filterRegex = providerConfig.TryGetValue("filterRegex", out var filterRegexObj) ? filterRegexObj?.ToString() : null;
                            Dictionary<string, string> sensorNameOverrides = new Dictionary<string, string>();

                            if (providerConfig.TryGetValue("sensorNameOverrides", out var overridesObj) && overridesObj is Dictionary<object, object> rawOverrides)
                            {
                                foreach (var keyValuePair in rawOverrides)
                                {
                                    if (keyValuePair.Key is string key && keyValuePair.Value is string value)
                                    {
                                        sensorNameOverrides[key] = value;
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Warning: Invalid type for sensor name override. Key: '{keyValuePair.Key?.GetType()}', Value: '{keyValuePair.Value?.GetType()}'. Skipping entry.");
                                    }
                                }
                            }

                            foreach (var paramInfo in parameters)
                            {
                                if (paramInfo.Name == "friendlyName")
                                {
                                    constructorArgs.Add(friendlyName);
                                }
                                else if (paramInfo.Name == "filterRegex")
                                {
                                    constructorArgs.Add(filterRegex);
                                }
                                else if (paramInfo.Name == "sensorNameOverrides")
                                {
                                    constructorArgs.Add(sensorNameOverrides);
                                }
                                else if (providerConfig.TryGetValue(paramInfo.Name, out var configValue))
                                {
                                    try
                                    {
                                        constructorArgs.Add(Convert.ChangeType(configValue, paramInfo.ParameterType));
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
                        }
                        else if (typeName == "HwMonLinux.NvidiaSmiSensorDataProvider, HwmONlinuxServer")
                        {
                            string friendlyName = providerConfig.TryGetValue("friendlyName", out var friendlyNameObj) ? friendlyNameObj?.ToString() : null;
                            List<string> queriedSensors = new List<string>();
                            if (providerConfig.TryGetValue("queriedSensors", out var queriedSensorsObj) && queriedSensorsObj is List<object> rawQueriedSensors)
                            {
                                foreach (var item in rawQueriedSensors)
                                {
                                    if (item is string sensor)
                                    {
                                        queriedSensors.Add(sensor);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Warning: Invalid type in queriedSensors list. Expected string, got '{item?.GetType()}'. Skipping entry.");
                                    }
                                }
                            }

                            Dictionary<string, string> sensorNameOverrides = new Dictionary<string, string>();
                            if (providerConfig.TryGetValue("sensorNameOverrides", out var overridesObj) && overridesObj is Dictionary<object, object> rawOverrides)
                            {
                                foreach (var keyValuePair in rawOverrides)
                                {
                                    if (keyValuePair.Key is string key && keyValuePair.Value is string value)
                                    {
                                        sensorNameOverrides[key] = value;
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Warning: Invalid type for NVIDIA SMI sensor name override. Key: '{keyValuePair.Key?.GetType()}', Value: '{keyValuePair.Value?.GetType()}'. Skipping entry.");
                                    }
                                }
                            }

                            foreach (var paramInfo in parameters)
                            {
                                if (paramInfo.Name == "friendlyName")
                                {
                                    constructorArgs.Add(friendlyName);
                                }
                                else if (paramInfo.Name == "queriedSensors")
                                {
                                    constructorArgs.Add(queriedSensors);
                                }
                                else if (paramInfo.Name == "sensorNameOverrides")
                                {
                                    constructorArgs.Add(sensorNameOverrides);
                                }
                                else if (providerConfig.TryGetValue(paramInfo.Name, out var configValue))
                                {
                                    try
                                    {
                                        constructorArgs.Add(Convert.ChangeType(configValue, paramInfo.ParameterType));
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
                        }
                        else
                        {
                            foreach (var paramInfo in parameters)
                            {
                                if (providerConfig.TryGetValue(paramInfo.Name, out var configValue))
                                {
                                    try
                                    {
                                        constructorArgs.Add(Convert.ChangeType(configValue, paramInfo.ParameterType));
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
                        }

                        if (canCreate)
                        {
                            var instance = Activator.CreateInstance(type, constructorArgs.ToArray()) as ISensorDataProvider;
                            if (instance != null)
                            {
                                sensorDataProviders.Add(instance);
                                Console.WriteLine($"Loaded sensor provider: {instance.FriendlyName} ({instance.Name})");
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
            }

            // Create and start web server
            var webServer = new WebServer(config.WebServer.Host, config.WebServer.Port, config.WebServer.ContentRoot, sensorDataStore, sensorDataProviders);
            await webServer.StartAsync();

            Console.WriteLine("Press any key to stop the server.");
            Console.ReadLine();

            await webServer.StopAsync();
        }
    }
}