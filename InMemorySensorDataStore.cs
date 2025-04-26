using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly int _retentionSeconds;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ReusableSensorData>> _sensorDataQueues = new();
        private readonly ConcurrentBag<ReusableSensorData> _sensorDataPool = new();
        private readonly ConcurrentBag<Dictionary<string, float>> _valueDictionaryPool = [];

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
            Dictionary<string, float> values = _valueDictionaryPool.TryTake(out var pooledDictionary) ? pooledDictionary : new Dictionary<string, float>();

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
                return queue.ToList();
            }
            return Enumerable.Empty<SensorData>();
        }

    private void CleanupOldData(string sensorIdentifier)
    {
        if (_sensorDataQueues.TryGetValue(sensorIdentifier, out var queue))
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-_retentionSeconds);
            //Console.WriteLine($"[{DateTime.Now}][Cleanup - {sensorIdentifier}] Cutoff: {cutoff:O}, Queue Size: {queue.Count}");
            ReusableSensorData oldest;
            int removedCount = 0;
            while (queue.TryPeek(out oldest) && oldest.Timestamp < cutoff)
            {
                if (queue.TryDequeue(out var toReturn))
                {
                    //Console.WriteLine($"[{DateTime.Now}][Cleanup - {sensorIdentifier}] Removed: {toReturn.Timestamp:O}");
                    if (toReturn.Values != null)
                    {
                        _valueDictionaryPool.Add(toReturn.Values);
                        toReturn.Values = null;
                    }
                    _sensorDataPool.Add(toReturn);
                    removedCount++;
                }
                else
                {
                    //Console.WriteLine($"[{DateTime.Now}][Cleanup - {sensorIdentifier}] Dequeue failed for: {oldest?.Timestamp:O}");
                    break; // Avoid potential infinite loop
                }
            }
            //Console.WriteLine($"[{DateTime.Now}][Cleanup - {sensorIdentifier}] Removed {removedCount} items. New Queue Size: {queue.Count}");
        }
    }

        private class ReusableSensorData : SensorData
        {
            // Inherits Timestamp and Values
        }
    }
}