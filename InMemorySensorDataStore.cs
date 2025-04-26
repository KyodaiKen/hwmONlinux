using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly int _retentionSeconds;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ReusableSensorData>> _sensorDataQueues = new ConcurrentDictionary<string, ConcurrentQueue<ReusableSensorData>>();
        private readonly ConcurrentBag<ReusableSensorData> _sensorDataPool = new ConcurrentBag<ReusableSensorData>();

        private readonly ConcurrentBag<Dictionary<string, object>> _valueDictionaryPool = new ConcurrentBag<Dictionary<string, object>>();

        public InMemorySensorDataStore(int retentionSeconds)
        {
            _retentionSeconds = retentionSeconds;
        }

        public void Store(string sensorIdentifier, SensorData data)
        {
            if (!_sensorDataQueues.TryGetValue(sensorIdentifier, out var queue))
            {
                _sensorDataQueues.TryAdd(sensorIdentifier, new ConcurrentQueue<ReusableSensorData>());
            }

            ReusableSensorData reusableData = _sensorDataPool.TryTake(out var pooledData) ? pooledData : new ReusableSensorData();
            Dictionary<string, object> values = _valueDictionaryPool.TryTake(out var pooledDictionary) ? pooledDictionary : new Dictionary<string, object>();

            values.Clear();
            foreach (var kvp in data.Values)
            {
                values[kvp.Key] = kvp.Value;
            }

            reusableData.Timestamp = data.Timestamp;
            reusableData.Values = values;

            _sensorDataQueues[sensorIdentifier].Enqueue(reusableData);
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
                // Return a view over the queue's elements without creating a new list immediately
                return queue;
            }
            return Enumerable.Empty<SensorData>();
        }

        private void CleanupOldData(string sensorIdentifier)
        {
            if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var queue))
            {
                DateTime cutoff = DateTime.UtcNow.AddSeconds(-_retentionSeconds);
                ReusableSensorData oldest;
                while (queue.TryPeek(out oldest) && oldest.Timestamp < cutoff)
                {
                    if (queue.TryDequeue(out var toReturn))
                    {
                        // Prepare the dequeued object for potential reuse
                        toReturn.Values = null; // Clear reference for safety
                        _sensorDataPool.Add(toReturn);
                    }
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