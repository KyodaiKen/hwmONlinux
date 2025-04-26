using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly int _retentionSeconds;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ReusableSensorData>> _sensorDataQueues = [];
        private readonly ConcurrentBag<ReusableSensorData> _sensorDataPool = [];

        private Dictionary<string, object> _values;
        private ReusableSensorData? _oldest = new();
        private ConcurrentQueue<ReusableSensorData> _queue = new();

        private readonly ConcurrentBag<Dictionary<string, object>> _valueDictionaryPool = [];

        public InMemorySensorDataStore(int retentionSeconds)
        {
            _retentionSeconds = retentionSeconds;
        }

        public void Store(string sensorIdentifier, SensorData data)
        {
            if (!_sensorDataQueues.TryGetValue(sensorIdentifier, out _))
            {
                _sensorDataQueues.TryAdd(sensorIdentifier, []);
            }

            ReusableSensorData reusableData = _sensorDataPool.TryTake(out var pooledData) ? pooledData : new ReusableSensorData();
            _values = _valueDictionaryPool.TryTake(out var pooledDictionary) ? pooledDictionary : [];

            _values.Clear();
            foreach (var kvp in data.Values)
            {
                _values[kvp.Key] = kvp.Value;
            }

            reusableData.Timestamp = data.Timestamp;
            reusableData.Values = _values;

            _sensorDataQueues[sensorIdentifier].Enqueue(reusableData);
            CleanupOldData(sensorIdentifier);
        }

        public IEnumerable<SensorData> GetAll(string sensorIdentifier)
        {
            if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var _queue))
            {
                // Return a view over the queue's elements without creating a new list immediately
                return _queue;
            }
            return Enumerable.Empty<SensorData>();
        }

        private void CleanupOldData(string sensorIdentifier)
        {
            if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var _queue))
            {
                DateTime cutoff = DateTime.UtcNow.AddSeconds(-_retentionSeconds);
                while (_queue.TryPeek(out _oldest) && _oldest.Timestamp < cutoff)
                {
                    _queue.TryDequeue(out _);
                }
            }
        }

        // Dedicated reusable class to minimize allocations
        private class ReusableSensorData : SensorData
        {
            // Inherits Timestamp and Values
        }
    }
}