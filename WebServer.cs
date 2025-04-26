using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Buffers;

namespace HwMonLinux
{
    public class WebServer
    {
        private readonly HttpListener _listener = new();
        private readonly string _contentRoot;
        private readonly InMemorySensorDataStore _sensorDataStore;
        private readonly List<ISensorDataProvider> _sensorDataProviders;
        private readonly List<SensorGroupDefinition> _sensorGroups;
        private static readonly ThreadLocal<Dictionary<string, List<SensorData>>> _sensorDataByNameCache = new(() => new());

        private CancellationTokenSource _cts;

        // Reusable buffer writer to minimize allocations
        private readonly ArrayBufferWriter<byte> _bufferWriter = new();
        private Utf8JsonWriter _jsonWriter;

        public WebServer(string host, int port, string contentRoot, int sensorRetentionSeconds, List<ISensorDataProvider> sensorDataProviders, List<SensorGroupDefinition> sensorGroups)
        {
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _contentRoot = contentRoot;
            _sensorDataStore = new(sensorRetentionSeconds);
            _sensorDataProviders = sensorDataProviders;
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
                Console.WriteLine($"[{DateTime.Now}] {request.HttpMethod} '{request.Url.AbsolutePath}'");

                if (request.Url.AbsolutePath == "/sensors/all")
                {
                    await HandleAllSensorsDataRequestAsync(context);
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
            
            string filename;
            if (request.Url.AbsolutePath == "/")
            {
                filename = Path.GetFullPath(Path.Combine(_contentRoot, "dashboard.html"));
            }
            else
                filename = Path.GetFullPath(Path.Combine(_contentRoot, request.Url.AbsolutePath.TrimStart('/')));


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

    private async Task HandleAllSensorsDataRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;

        if (_sensorDataProviders.Count == 0) {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return;
        }

        _bufferWriter.Clear();
        using (_jsonWriter = new Utf8JsonWriter(_bufferWriter))
        {
            _jsonWriter.WriteStartObject();
            _jsonWriter.WritePropertyName("providers");
            _jsonWriter.WriteStartObject(); // Start of the providers object
            foreach (var provider in _sensorDataProviders)
            {
                _jsonWriter.WritePropertyName(provider.FriendlyName); // Provider Friendly Name
                _jsonWriter.WriteStartObject(); // Start of the sensor data for the provider

                Dictionary<string, List<SensorData>> sensorDataBySensorName = _sensorDataByNameCache.Value!;
                sensorDataBySensorName.Clear();

                var allData = _sensorDataStore.GetAll(provider.Name);
                if (allData != null)
                {
                    foreach (var sd in allData)
                    {
                        if (sd.Values != null)
                        {
                            foreach (var kvp in sd.Values)
                            {
                                if (!sensorDataBySensorName.TryGetValue(kvp.Key, out var list))
                                {
                                    list = new List<SensorData>();
                                    sensorDataBySensorName[kvp.Key] = list;
                                }
                                list.Add(sd);
                            }
                        }
                    }

                    foreach (var sensorNameKvp in sensorDataBySensorName)
                    {
                        _jsonWriter.WritePropertyName(sensorNameKvp.Key);
                        _jsonWriter.WriteStartArray();
                        foreach (var dataPoint in sensorNameKvp.Value.OrderBy(d => d.Timestamp))
                        {
                            _jsonWriter.WriteStartObject();
                            _jsonWriter.WriteString("Timestamp", dataPoint.Timestamp.ToString("O"));
                            _jsonWriter.WritePropertyName("Value");
                            JsonSerializer.Serialize(_jsonWriter, dataPoint.Values[sensorNameKvp.Key]); // Access Value from SensorData
                            _jsonWriter.WriteEndObject();
                        }
                        _jsonWriter.WriteEndArray();
                    }
                }

                _jsonWriter.WriteEndObject(); // End of the sensor data for the provider
            }
            _jsonWriter.WriteEndObject(); // End of the providers object

            _jsonWriter.WritePropertyName("sensorGroups");
            _jsonWriter.WriteStartArray();
            foreach (var g in _sensorGroups)
            {
                _jsonWriter.WriteStartObject();
                _jsonWriter.WriteString("name", g.Name);
                _jsonWriter.WriteString("friendlyName", g.FriendlyName);
                _jsonWriter.WritePropertyName("sensorIdentifiers");
                JsonSerializer.Serialize(_jsonWriter, g.SensorIdentifiers);
                _jsonWriter.WriteEndObject();
            }
            _jsonWriter.WriteEndArray();

            _jsonWriter.WriteEndObject();
            await _jsonWriter.FlushAsync();
            byte[] buffer = _bufferWriter.WrittenSpan.ToArray();
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
                        if (data != null && data.Values != null)
                        {
                            data.Timestamp = DateTime.UtcNow;
                            _sensorDataStore.Store(provider.Name, data); // Store in the history
                            Console.WriteLine($"[{DateTime.Now}] Sensor '{provider.Name}': Gathered data.");
                        }
                        data = null;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error polling sensor '{provider.Name}': {ex.Message}");
                    }
                }

                sw.Stop();
                int remainingDelayMs = Math.Max(0, (int)(1000 - sw.ElapsedMilliseconds));

                if (remainingDelayMs > 0)
                {
                    await Task.Delay(remainingDelayMs, cancellationToken);
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now}] Warning: Polling took longer than the desired interval ({sw.ElapsedMilliseconds:F3}ms >= 1000ms).");
                    await Task.Delay(1, cancellationToken);
                }
            }
        }
    }
}