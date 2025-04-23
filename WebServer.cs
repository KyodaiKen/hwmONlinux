using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _contentRoot;
        private readonly InMemorySensorDataStore _sensorDataStore;
        private readonly List<ISensorDataProvider> _sensorDataProviders;
        private CancellationTokenSource _cts;

        public WebServer(string host, int port, string contentRoot, InMemorySensorDataStore sensorDataStore, List<ISensorDataProvider> sensorDataProviders)
        {
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _contentRoot = contentRoot;
            _sensorDataStore = sensorDataStore;
            _sensorDataProviders = sensorDataProviders;
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
                    _ = Task.Run(() => ProcessRequestAsync(context)); // Handle jede Anfrage in einem eigenen Task
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    // Operation aborted (Listener wurde gestoppt)
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
            var sensorData = _sensorDataStore.GetLatest(sensorName);

            response.ContentType = "application/json";
            if (sensorData != null)
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                string json = JsonSerializer.Serialize(sensorData);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                string notFoundJson = JsonSerializer.Serialize(new { message = $"Sensor '{sensorName}' data not found or expired." });
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
                var allData = _sensorDataStore.GetAll(provider.Name); // Still using provider.Name to fetch data

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
                                    Values = new Dictionary<string, object> { { sensorName, value } }
                                });
                            }
                        }
                    }
                    allSensorData[provider.FriendlyName] = sensorDataForProvider; // Use FriendlyName as the key in the JSON
                }
                else
                {
                    allSensorData[provider.FriendlyName] = new Dictionary<string, List<object>>(); // Use FriendlyName even for empty data
                }
            }

            string json = JsonSerializer.Serialize(allSensorData);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
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
                        if (data != null)
                        {
                            _sensorDataStore.Store(provider.Name, data); // Still store by the unique Name
                            Console.WriteLine($"[{DateTime.Now}] Sensor '{provider.Name}': Gathered data.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error polling sensor '{provider.Name}': {ex.Message}");
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
                    // Optionally add a small delay to avoid spinning the CPU too much
                    await Task.Delay(1, cancellationToken);
                }
            }
        }
    }
}