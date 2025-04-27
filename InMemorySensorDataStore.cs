using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly int _retentionSeconds;
        // Provider -> SensorName -> List of (Timestamp, Value)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<(DateTime Timestamp, object Value)>>> _sensorDataByProviderAndSensor;

        public InMemorySensorDataStore(int retentionSeconds)
        {
            _retentionSeconds = retentionSeconds;
            _sensorDataByProviderAndSensor = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<(DateTime Timestamp, object Value)>>>();
        }

        public void Store(string providerName, SensorData rawData)
        {
            if (rawData?.Values == null) return;

            var providerStore = _sensorDataByProviderAndSensor.GetOrAdd(providerName, _ => new ConcurrentDictionary<string, ConcurrentQueue<(DateTime Timestamp, object Value)>>());
            var timestamp = rawData.Timestamp;

            foreach (var kvp in rawData.Values)
            {
                var sensorName = kvp.Key;
                var value = kvp.Value;
                var queue = providerStore.GetOrAdd(sensorName, _ => new ConcurrentQueue<(DateTime Timestamp, object Value)>());
                queue.Enqueue((timestamp, value));

                // Clean up old data
                var cutoff = DateTime.UtcNow.AddSeconds(-_retentionSeconds);
                var itemsToRemove = new List<(DateTime Timestamp, object Value)>();
                foreach (var item in queue)
                {
                    if (item.Timestamp < cutoff)
                    {
                        itemsToRemove.Add(item);
                    }
                    else
                    {
                        // Since items are added in increasing timestamp order,
                        // once we hit a recent item, we can stop checking.
                        break;
                    }
                }

                // Dequeue the old items
                foreach (var _ in itemsToRemove)
                {
                    (DateTime Timestamp, object Value) dequeuedItem; // Declare a variable for out
                    queue.TryDequeue(out dequeuedItem);
                }
                itemsToRemove = null;
            }
        }

        public Dictionary<string, List<(DateTime Timestamp, object Value)>> GetAllGroupedBySensor(string providerName)
        {
            var result = new Dictionary<string, List<(DateTime Timestamp, object Value)>>();
            if (_sensorDataByProviderAndSensor.TryGetValue(providerName, out var providerStore))
            {
                foreach (var kvp in providerStore)
                {
                    var queue = kvp.Value;
                    var list = new List<(DateTime Timestamp, object Value)>(queue.Count);
                    foreach (var item in queue)
                    {
                        list.Add(item);
                    }
                    list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                    result[kvp.Key] = list;
                }
            }
            return result;
        }
    }
}