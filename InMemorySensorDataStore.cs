using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(SensorData Data, DateTime Timestamp)>> _sensorDataHistory = new ConcurrentDictionary<string, ConcurrentQueue<(SensorData, DateTime)>>();
        private readonly int _dataRetentionSeconds;

        public InMemorySensorDataStore(int dataRetentionSeconds)
        {
            _dataRetentionSeconds = dataRetentionSeconds;
            Task.Run(async () => await CleanupExpiredDataAsync());
        }

        public void Store(string sensorName, SensorData data)
        {
            var newDataPoint = (data, DateTime.UtcNow);
            _sensorDataHistory.AddOrUpdate(
                sensorName,
                (key) => { // Factory function to create a new ConcurrentQueue
                    var queue = new ConcurrentQueue<(SensorData, DateTime)>();
                    queue.Enqueue(newDataPoint);
                    return queue;
                },
                (key, existingQueue) => { existingQueue.Enqueue(newDataPoint); return existingQueue; });
        }

        public SensorData? GetLatest(string sensorName)
        {
            if (_sensorDataHistory.TryGetValue(sensorName, out var dataQueue))
            {
                var latest = dataQueue.LastOrDefault(item => (DateTime.UtcNow - item.Timestamp).TotalSeconds <= _dataRetentionSeconds);
                return latest.Data;
            }
            return null;
        }

        public List<SensorData> GetAll(string sensorName)
        {
            if (_sensorDataHistory.TryGetValue(sensorName, out var dataQueue))
            {
                var currentTime = DateTime.UtcNow;
                return dataQueue
                    .Where(item => (currentTime - item.Timestamp).TotalSeconds <= _dataRetentionSeconds)
                    .Select(item => item.Data)
                    .ToList();
            }
            return new List<SensorData>();
        }

        private async Task CleanupExpiredDataAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, _dataRetentionSeconds)));
                var currentTime = DateTime.UtcNow;
                foreach (var sensorName in _sensorDataHistory.Keys.ToList())
                {
                    if (_sensorDataHistory.TryGetValue(sensorName, out var dataQueue))
                    {
                        var nonExpiredData = new ConcurrentQueue<(SensorData Data, DateTime Timestamp)>(
                            dataQueue.Where(item => (currentTime - item.Timestamp).TotalSeconds <= _dataRetentionSeconds));
                        _sensorDataHistory.TryUpdate(sensorName, nonExpiredData, dataQueue);
                    }
                }
            }
        }
    }
}