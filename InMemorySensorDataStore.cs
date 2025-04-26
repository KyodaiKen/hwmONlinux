using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly int _retentionSeconds;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<SensorData>> _sensorDataQueues = new ConcurrentDictionary<string, ConcurrentQueue<SensorData>>();
        private readonly ConcurrentBag<SensorData> _sensorDataPool = new ConcurrentBag<SensorData>();
        private readonly ConcurrentBag<Dictionary<string, object>> _valueDictionaryPool = new ConcurrentBag<Dictionary<string, object>>();

        public InMemorySensorDataStore(int retentionSeconds)
        {
            _retentionSeconds = retentionSeconds;
        }

        public void Store(string sensorIdentifier, SensorData data)
        {
            if (!_sensorDataQueues.ContainsKey(sensorIdentifier))
            {
                _sensorDataQueues.TryAdd(sensorIdentifier, new ConcurrentQueue<SensorData>());
            }

            SensorData sensorData = _sensorDataPool.TryTake(out var pooledSensorData) ? pooledSensorData : new SensorData();
            Dictionary<string, object> values = _valueDictionaryPool.TryTake(out var pooledDictionary) ? pooledDictionary : new Dictionary<string, object>();

            values.Clear();
            foreach (var kvp in data.Values)
            {
                values[kvp.Key] = kvp.Value;
            }

            sensorData.Timestamp = data.Timestamp;
            sensorData.Values = values;

            _sensorDataQueues[sensorIdentifier].Enqueue(sensorData);
            CleanupOldData(sensorIdentifier);
        }

        public SensorData? GetLatest(string sensorIdentifier)
        {
            if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var queue) && queue.LastOrDefault() != null)
            {
                return queue.LastOrDefault();
            }
            return null;
        }

        public IEnumerable<SensorData> GetAll(string sensorIdentifier)
        {
            if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var queue))
            {
                return queue.ToList();
            }
            return Enumerable.Empty<SensorData>();
        }

        private void CleanupOldData(string sensorIdentifier)
        {
            if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var queue))
            {
                DateTime cutoff = DateTime.UtcNow.AddSeconds(-_retentionSeconds);
                while (queue.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
                {
                    queue.TryDequeue(out _);
                }
            }
        }
    }
}