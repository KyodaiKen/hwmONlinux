using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

namespace HwMonLinux
{
    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _contentRoot;
        private readonly InMemorySensorDataStore _sensorDataStore;
        private readonly List<ISensorDataProvider> _sensorDataProviders;
        private readonly ConcurrentDictionary<string, OrderedDictionary> _groupedSensorData = new ConcurrentDictionary<string, OrderedDictionary>();
        private readonly List<SensorGroupDefinition> _sensorGroups;
        private CancellationTokenSource _cts;
        private readonly StringBuilder _stringBuilder = new StringBuilder(2048); // Reuse StringBuilder for JSON
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _latestSensorDataReusable = new ConcurrentDictionary<string, Dictionary<string, object>>();

        public WebServer(string host, int port, string contentRoot, InMemorySensorDataStore sensorDataStore, List<ISensorDataProvider> sensorDataProviders, Dictionary<string, OrderedDictionary> groupedSensorData, List<SensorGroupDefinition> sensorGroups)
        {
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _contentRoot = contentRoot;
            _sensorDataStore = sensorDataStore;
            _sensorDataProviders = sensorDataProviders;
            if (groupedSensorData != null)
            {
                foreach (var kvp in groupedSensorData)
                {
                    _groupedSensorData.TryAdd(kvp.Key, kvp.Value);
                }
            }
            _sensorGroups = sensorGroups;
        }

        public async Task StartAsync()
        {
            _listener.Start();
            Console.WriteLine("Web server started! Waiting for requests...");
            _cts = new CancellationTokenSource();

            // Start sensor polling
            _ = Task.Run(async () => await PollSensorsAsync(_cts.Token));
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
            var allData = _sensorDataStore.GetAll(sensorName);

            response.ContentType = "application/json";
            var bufferWriter = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                writer.WriteStartArray();
                if (allData != null && allData.Any())
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    foreach (var sd in allData)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("Timestamp", sd.Timestamp.ToString("O"));
                        foreach (var kvp in sd.Values)
                        {
                            writer.WritePropertyName(kvp.Key);
                            JsonSerializer.Serialize(writer, kvp.Value);
                        }
                        writer.WriteEndObject();
                    }
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    writer.WriteStartObject();
                    writer.WriteString("message", $"Sensor data for '{sensorName}' not found or expired.");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                await writer.FlushAsync();
                byte[] buffer = bufferWriter.WrittenSpan.ToArray();
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private async Task HandleAllSensorsDataRequestAsync(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;

            var bufferWriter = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("providers");
                writer.WriteStartObject(); // Start of the providers object
                foreach (var provider in _sensorDataProviders)
                {
                    writer.WritePropertyName(provider.FriendlyName); // Provider Friendly Name
                    writer.WriteStartObject(); // Start of the sensor data for the provider

                    var allData = _sensorDataStore.GetAll(provider.Name);
                    if (allData != null)
                    {
                        var sensorDataBySensorName = allData
                            .Where(sd => sd.Values != null)
                            .SelectMany(sd => sd.Values.Select(kvp => new { sd.Timestamp, SensorName = kvp.Key, Value = kvp.Value }))
                            .GroupBy(item => item.SensorName)
                            .ToDictionary(
                                group => group.Key,
                                group => group.Select(item => new { item.Timestamp, item.Value })
                                            .OrderBy(item => item.Timestamp)
                                            .ToList()
                            );

                        foreach (var sensorNameKvp in sensorDataBySensorName)
                        {
                            writer.WritePropertyName(sensorNameKvp.Key);
                            JsonSerializer.Serialize(writer, sensorNameKvp.Value);
                        }
                    }

                    writer.WriteEndObject(); // End of the sensor data for the provider
                }
                writer.WriteEndObject(); // End of the providers object

                writer.WritePropertyName("sensorGroups");
                writer.WriteStartArray();
                foreach (var g in _sensorGroups)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", g.Name);
                    writer.WriteString("friendlyName", g.FriendlyName);
                    writer.WritePropertyName("sensorIdentifiers");
                    JsonSerializer.Serialize(writer, g.SensorIdentifiers);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
                await writer.FlushAsync();
                byte[] buffer = bufferWriter.WrittenSpan.ToArray();
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private async Task HandleGroupSensorsDataRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string groupName = request.Url.AbsolutePath.Substring("/sensors/group/".Length);

            response.ContentType = "application/json";
            var bufferWriter = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                if (_groupedSensorData.TryGetValue(groupName, out var groupData))
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    JsonSerializer.Serialize(writer, groupData);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    writer.WriteStartObject();
                    writer.WriteString("message", $"Sensor group '{groupName}' not found.");
                    writer.WriteEndObject();
                }
                await writer.FlushAsync();
                byte[] buffer = bufferWriter.WrittenSpan.ToArray();
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

                foreach (var provider in _sensorDataProviders)
                {
                    try
                    {
                        var data = provider.GetSensorData();
                        data.Timestamp = DateTime.UtcNow;
                        if (data != null && data.Values != null)
                        {
                            _sensorDataStore.Store(provider.Name, data); // Store in the history
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
                    var groupedData = _groupedSensorData.GetOrAdd(groupDef.Name, (_) => new OrderedDictionary());
                    groupedData.Clear(); // Clear before updating in each poll

                    foreach (var identifier in groupDef.SensorIdentifiers)
                    {
                        foreach (var providerName in _latestSensorDataReusable.Keys)
                        {
                            if (_latestSensorDataReusable.TryGetValue(providerName, out var providerLatestData))
                            {
                                foreach (var sensorName in providerLatestData.Keys)
                                {
                                    string fullIdentifier = $"{providerName} {sensorName}";
                                    if (Regex.IsMatch(fullIdentifier, identifier, RegexOptions.IgnoreCase))
                                    {
                                        if (!groupedData.Contains(fullIdentifier))
                                        {
                                            groupedData[fullIdentifier] = providerLatestData[sensorName];
                                            goto NextIdentifier; // Move to the next identifier after finding a match
                                        }
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