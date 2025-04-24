using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Specialized; // Add this using statement

namespace HwMonLinux
{
    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _contentRoot;
        private readonly InMemorySensorDataStore _sensorDataStore;
        private readonly List<ISensorDataProvider> _sensorDataProviders;
        private readonly Dictionary<string, OrderedDictionary> _groupedSensorData = new Dictionary<string, OrderedDictionary>(); // Use OrderedDictionary
        private readonly List<SensorGroupDefinition> _sensorGroups;
        private CancellationTokenSource _cts;

        public WebServer(string host, int port, string contentRoot, InMemorySensorDataStore sensorDataStore, List<ISensorDataProvider> sensorDataProviders, Dictionary<string, OrderedDictionary> groupedSensorData, List<SensorGroupDefinition> sensorGroups)
        {
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _contentRoot = contentRoot;
            _sensorDataStore = sensorDataStore;
            _sensorDataProviders = sensorDataProviders;
            _groupedSensorData = groupedSensorData;
            _sensorGroups = sensorGroups;
        }

        public async Task StartAsync()
        {
            _listener.Start();
            Console.WriteLine("Web server started! Waiting for requests...");
            _cts = new CancellationTokenSource();

            // Start sensor polling
            Task.Run(async () => await PollSensorsAsync(_cts.Token));
            await Task.Run(() => ListenAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            _listener.Stop();
            Console.WriteLine("Web server stopped.");
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context)); // Handle each request in its own Task
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    // Operation aborted (Listener was stopped)
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving request: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                Console.WriteLine($"[{DateTime.Now}] {request.HttpMethod} {request.Url.AbsolutePath}");

                if (request.Url.AbsolutePath == "/sensors/all")
                {
                    await HandleAllSensorsDataRequestAsync(context);
                }
                else if (request.Url.AbsolutePath.StartsWith("/sensors/group/"))
                {
                    await HandleGroupSensorsDataRequestAsync(context);
                }
                else if (request.Url.AbsolutePath.StartsWith("/sensors/"))
                {
                    await HandleSingleSensorDataRequestAsync(context);
                }
                else
                {
                    await HandleStaticFileRequestAsync(context);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while processing request: {ex.Message}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private async Task HandleStaticFileRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string filename = Path.GetFullPath(Path.Combine(_contentRoot, request.Url.AbsolutePath.TrimStart('/')));

            if (File.Exists(filename))
            {
                string contentType = GetContentType(filename);
                response.ContentType = contentType;
                response.StatusCode = (int)HttpStatusCode.OK;
                using (var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true))
                {
                    await fileStream.CopyToAsync(response.OutputStream);
                }
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                string notFoundHtml = "<html><body><h1>404 Not Found</h1></body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(notFoundHtml);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private async Task HandleSingleSensorDataRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string sensorName = request.Url.AbsolutePath.Substring("/sensors/".Length);
            var allData = _sensorDataStore.GetAll(sensorName); // Use the raw sensor identifier (provider.Name)

            response.ContentType = "application/json";
            if (allData != null && allData.Any())
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                string json = JsonSerializer.Serialize(allData.SelectMany(sd => sd.Values.Select(kvp => new { Timestamp = sd.Timestamp, Name = kvp.Key, Value = kvp.Value })).ToList());
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                string notFoundJson = JsonSerializer.Serialize(new { message = $"Sensor data for '{sensorName}' not found or expired." });
                byte[] buffer = Encoding.UTF8.GetBytes(notFoundJson);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private async Task HandleAllSensorsDataRequestAsync(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;

            var allSensorData = new Dictionary<string, Dictionary<string, List<object>>>();

            foreach (var provider in _sensorDataProviders)
            {
                var sensorDataForProvider = new Dictionary<string, List<object>>();
                var allData = _sensorDataStore.GetAll(provider.Name);

                if (allData != null && allData.Any())
                {
                    foreach (var sensorReading in allData)
                    {
                        if (sensorReading?.Values != null)
                        {
                            foreach (var valuePair in sensorReading.Values)
                            {
                                var sensorName = valuePair.Key;
                                var value = valuePair.Value;

                                if (!sensorDataForProvider.ContainsKey(sensorName))
                                {
                                    sensorDataForProvider[sensorName] = new List<object>();
                                }

                                sensorDataForProvider[sensorName].Add(new
                                {
                                    Timestamp = sensorReading.Timestamp,
                                    Value = value
                                });
                            }
                        }
                    }
                    allSensorData[provider.FriendlyName] = sensorDataForProvider;
                }
                else
                {
                    allSensorData[provider.FriendlyName] = new Dictionary<string, List<object>>();
                }
            }

            // Embed sensor group information in the JSON response
            var responseData = new
            {
                sensorGroups = _sensorGroups.Select(g => new
                {
                    name = g.Name,
                    friendlyName = g.FriendlyName,
                    sensorIdentifiers = g.SensorIdentifiers // Ensure this is included!
                }),
                providers = allSensorData
            };

            string json = JsonSerializer.Serialize(responseData);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task HandleGroupSensorsDataRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string groupName = request.Url.AbsolutePath.Substring("/sensors/group/".Length);

            response.ContentType = "application/json";
            if (_groupedSensorData.TryGetValue(groupName, out var groupData))
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                string json = JsonSerializer.Serialize(groupData);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                string notFoundJson = JsonSerializer.Serialize(new { message = $"Sensor group '{groupName}' not found." });
                byte[] buffer = Encoding.UTF8.GetBytes(notFoundJson);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }


        private string GetContentType(string filename)
        {
            string extension = Path.GetExtension(filename).ToLowerInvariant();
            return extension switch
            {
                ".html" => "text/html",
                ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "application/octet-stream",
            };
        }

        private async Task PollSensorsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Stopwatch sw = Stopwatch.StartNew();
                var latestSensorData = new Dictionary<string, Dictionary<string, object>>(); // Store latest data by provider

                foreach (var provider in _sensorDataProviders)
                {
                    try
                    {
                        var data = provider.GetSensorData();
                        if (data != null)
                        {
                            _sensorDataStore.Store(provider.Name, data);
                            latestSensorData[provider.Name] = data.Values;
                            Console.WriteLine($"[{DateTime.Now}] Sensor '{provider.Name}': Gathered data.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error polling sensor '{provider.Name}': {ex.Message}");
                    }
                }

                // Group the latest sensor data, strictly maintaining the order from SensorIdentifiers
                foreach (var groupDef in _sensorGroups)
                {
                    if (!_groupedSensorData.ContainsKey(groupDef.Name))
                    {
                        _groupedSensorData[groupDef.Name] = new OrderedDictionary(); // Initialize as OrderedDictionary
                    }
                    _groupedSensorData[groupDef.Name].Clear(); // Clear previous data for the group

                    foreach (var identifier in groupDef.SensorIdentifiers)
                    {
                        foreach (var providerName in latestSensorData.Keys)
                        {
                            var providerLatestData = latestSensorData[providerName];
                            foreach (var sensorName in providerLatestData.Keys)
                            {
                                string fullIdentifier = $"{providerName} {sensorName}";
                                if (Regex.IsMatch(fullIdentifier, identifier, RegexOptions.IgnoreCase))
                                {
                                    if (!_groupedSensorData[groupDef.Name].Contains(fullIdentifier))
                                    {
                                        _groupedSensorData[groupDef.Name][fullIdentifier] = providerLatestData[sensorName];
                                        goto NextIdentifier; // Move to the next identifier after finding a match
                                    }
                                }
                            }
                        }
                        NextIdentifier:; // Label to jump to
                    }
                }

                sw.Stop();
                double pollingTimeMs = sw.ElapsedMilliseconds;
                double delayTimeMs = TimeSpan.FromSeconds(1).TotalMilliseconds;
                int remainingDelayMs = Math.Max(0, (int)(delayTimeMs - pollingTimeMs));

                if (remainingDelayMs > 0)
                {
                    await Task.Delay(remainingDelayMs, cancellationToken);
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now}] Warning: Polling took longer than the desired interval ({(pollingTimeMs / 1000.0):F3}s >= 1s).");
                    await Task.Delay(1, cancellationToken);
                }
            }
        }
    }
}