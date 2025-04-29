using System;

namespace HwMonLinux
{
    public class InMemorySensorDataStore
    {
        // Parameters
        private readonly int _retentionSeconds;
        private readonly (string, string[])[] _sensorIndex;

        // Sensor data
        private readonly (DateTime, float)[][][] _data;
        private readonly int[][] _counters;

        public InMemorySensorDataStore(int retentionSeconds, (string, string[])[] sensorProvidersAndTheirSensors)
        {
            _retentionSeconds = retentionSeconds;
            _sensorIndex = sensorProvidersAndTheirSensors;

            // Allocate memory for all providers and their exposed sensors
            _data = new (DateTime, float)[_sensorIndex.Length][][];
            _counters = new int[_sensorIndex.Length][];

            // Initialize the providers
            for (int p = 0; p < _sensorIndex.Length; p++)
            {
                _data[p] = new (DateTime, float)[_sensorIndex[p].Item2.Length][];
                _counters[p] = new int[_sensorIndex[p].Item2.Length];

                // Initialize the provider's sensors
                for (int s = 0; s < _sensorIndex[p].Item2.Length; s++)
                {
                    _data[p][s] = new (DateTime, float)[retentionSeconds];
                    _counters[p][s] = 0;
                }
            }
        }

        public void StoreSensorDataFromProvider(string providerName, (string, float)[] providedData)
        {
            for (int p = 0; p < _sensorIndex.Length; p++)
            {
                if (_sensorIndex[p].Item1 == providerName)
                {
                    for (int s = 0; s < _sensorIndex[p].Item2.Length; s++)
                    {
                        for (int ps = 0; ps < providedData.Length; ps++)
                        {
                            if (_sensorIndex[p].Item2[s] == providedData[ps].Item1)
                            {
                                // Set new value
                                _data[p][s][_counters[p][s]].Item1 = DateTime.UtcNow;
                                _data[p][s][_counters[p][s]].Item2 = providedData[s].Item2;

                                if (_counters[p][s] == _retentionSeconds - 1)
                                {
                                    // Shift array one to the left
                                    for (int v = 1; v < _retentionSeconds; v++)
                                    {
                                        _data[p][s][v - 1] = _data[p][s][v];
                                    }
                                }
                                else
                                {
                                    // Increment the counter
                                    _counters[p][s]++;
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        public bool GetSensorDataFromProvider(string providerName, out (DateTime, float)[][] data, out int[] counters)
        {
            // Find the sensor data for our provider and return it.
            for (int i = 0; i < _sensorIndex.Length; i++)
            {
                if (_sensorIndex[i].Item1 == providerName)
                {
                    counters = _counters[i];
                    data = _data[i];
                    return true;
                }
            }

            // Nothing has been found for given provider name
            counters = null;
            data = null;
            return false;
        }
    }
}