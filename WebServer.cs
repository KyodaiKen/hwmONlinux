using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class WebServer
    {
        private readonly HttpListener _listener = new();
        private readonly string _contentRoot;
        private readonly InMemorySensorDataStore _sensorDataStore;
        private readonly List<ISensorDataProvider> _sensorDataProviders;
        private readonly List<SensorGroupDefinition> _sensorGroups;
        private readonly (string, (string, string)[])[] _sensorIndex; // Receive the sensor index (originalName, label)

        private readonly byte[] notFoundErrHTML = { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0x3C, 0x62, 0x6F,
                                                    0x64, 0x79, 0x3E, 0x3C, 0x68, 0x31, 0x3E, 0x34, 0x30,
                                                    0x34, 0x20, 0x4E, 0x6F, 0x74, 0x20, 0x46, 0x6F, 0x75,
                                                    0x6E, 0x64, 0x3C, 0x2F, 0x68, 0x31, 0x3E, 0x3C, 0x2F,
                                                    0x62, 0x6F, 0x64, 0x79, 0x3E, 0x3C, 0x2F, 0x68, 0x74,
                                                    0x6D, 0x6C, 0x3E };

        private CancellationTokenSource _cts;

        // Reusable buffer writer to minimize allocations
        private readonly ArrayBufferWriter<byte> _bufferWriter = new();
        private Utf8JsonWriter _jsonWriter;

        public WebServer(string host, int port, string contentRoot, int sensorRetentionSeconds, List<ISensorDataProvider> sensorDataProviders, List<SensorGroupDefinition> sensorGroups, (string, (string, string)[])[] sensorIndex)
        {
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _contentRoot = contentRoot;
            _sensorDataProviders = sensorDataProviders;
            _sensorGroups = sensorGroups;
            _sensorIndex = sensorIndex;
            _sensorDataStore = new InMemorySensorDataStore(sensorRetentionSeconds, _sensorIndex.Select(p => (p.Item1, p.Item2.Select(l => l.Item1).ToArray())).ToArray()); // Initialize data store with original names
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
                response.ContentLength64 = notFoundErrHTML.Length;
                await response.OutputStream.WriteAsync(notFoundErrHTML, 0, notFoundErrHTML.Length);
            }
        }

        private async Task HandleAllSensorsDataRequestAsync(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;

            using (_jsonWriter = new Utf8JsonWriter(response.OutputStream))
            {
                _jsonWriter.WriteStartObject();
                _jsonWriter.WritePropertyName("providers");
                _jsonWriter.WriteStartObject(); // Start of the providers object

                foreach (var providerInfo in _sensorIndex)
                {
                    var provider = _sensorDataProviders.FirstOrDefault(p => p.Name == providerInfo.Item1);
                    _jsonWriter.WritePropertyName(providerInfo.Item1); // Original Provider Name (for matching with groups)
                    _jsonWriter.WriteStartObject(); // Start of the sensor data for the provider
                    _jsonWriter.WriteString("friendlyName", provider?.FriendlyName); // Add friendlyName

                    if (_sensorDataStore.GetSensorDataFromProvider(providerInfo.Item1, out var providerData, out var counters))
                    {
                        for (int s = 0; s < providerInfo.Item2.Length; s++)
                        {
                            string originalSensorName = providerInfo.Item2[s].Item1;
                            string sensorLabel = providerInfo.Item2[s].Item2;

                            _jsonWriter.WritePropertyName(originalSensorName); // Original Sensor Name
                            _jsonWriter.WriteStartObject(); // Start of the sensor object
                            _jsonWriter.WriteString("friendlyName", sensorLabel); // Add friendlyName for the sensor
                            _jsonWriter.WritePropertyName("data");
                            _jsonWriter.WriteStartArray(); // Start of the sensor data array
                            //Console.WriteLine($"counters[s] => {counters[s]}");
                            for (int i = 0; i < counters[s]; i++)
                            {
                                _jsonWriter.WriteStartObject();
                                _jsonWriter.WriteString("Timestamp", providerData[s][i].Item1.ToString("O"));
                                _jsonWriter.WriteNumber("Value", providerData[s][i].Item2);
                                _jsonWriter.WriteEndObject();
                            }
                            _jsonWriter.WriteEndArray(); // End of the sensor data array
                            _jsonWriter.WriteEndObject(); // End of the sensor object
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
                        if (provider.GetSensorData(out var sensorData))
                        {
                            if (sensorData != null && sensorData.Length > 0)
                            {
                                _sensorDataStore.StoreSensorDataFromProvider(provider.Name, sensorData);
                                Console.WriteLine($"[{DateTime.Now}] Provider: '{provider.Name}': Gathered data for {sensorData.Length} sensors.");
                            }
                            else
                            {
                                Console.WriteLine($"[{DateTime.Now}] Provider: '{provider.Name}': No sensor data received.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error polling sensors for provider '{provider.Name}': {ex.Message}");
                    }
                }
                GC.Collect();

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