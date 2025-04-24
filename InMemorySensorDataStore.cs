using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly int _retentionSeconds;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<SensorData>> _sensorData = new ConcurrentDictionary<string, ConcurrentQueue<SensorData>>();

        public InMemorySensorDataStore(int retentionSeconds)
        {
            _retentionSeconds = retentionSeconds;
        }

        public void Store(string sensorIdentifier, SensorData data)
        {
            if (!_sensorData.ContainsKey(sensorIdentifier))
            {
                _sensorData.TryAdd(sensorIdentifier, new ConcurrentQueue<SensorData>());
            }
            _sensorData[sensorIdentifier].Enqueue(data);
            CleanupOldData(sensorIdentifier);
        }

        public SensorData? GetLatest(string sensorIdentifier)
        {
            if (_sensorData.TryGetValue(sensorIdentifier, out var queue) && queue.LastOrDefault() != null)
            {
                return queue.LastOrDefault();
            }
            return null;
        }

        public IEnumerable<SensorData> GetAll(string sensorIdentifier)
        {
            if (_sensorData.TryGetValue(sensorIdentifier, out var queue))
            {
                return queue.ToList();
            }
            return Enumerable.Empty<SensorData>();
        }

        private void CleanupOldData(string sensorIdentifier)
        {
            if (_sensorData.TryGetValue(sensorIdentifier, out var queue))
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