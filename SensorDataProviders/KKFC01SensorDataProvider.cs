using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

namespace HwMonLinux
{
    public class KKFC01SensorDataProvider : ISensorDataProvider, IDisposable
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly string _queryCommand;
        private readonly string _identificationString;
        private SerialPort _serialPort;
        private readonly int _numEntries = 3;
        private readonly int _floatSize = 4;
        private const string IdentificationQuery = "i";
        private const int SerialTimeoutMs = 1000;

        public string Name => "KKFC01";
        public string FriendlyName { get; }

        //Persistent memory to avoid reallocations
        private byte[] _receivedBuffer;
        private int _bytesRead;
        private float[] _unpackedData;
        private SensorData _sensorData;

        public KKFC01SensorDataProvider(string friendlyName, string portName, int baudRate, string queryCommand, string identificationString)
        {
            FriendlyName = friendlyName;
            _portName = portName;
            _baudRate = baudRate;
            _queryCommand = queryCommand;
            _identificationString = identificationString;

            _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One);
            _serialPort.ReadTimeout = SerialTimeoutMs;
            _serialPort.WriteTimeout = SerialTimeoutMs;

            _receivedBuffer = new byte[_floatSize * _numEntries * 3];
            _unpackedData = new float[_numEntries * 3];

            if (!Connect())
            {
                Console.WriteLine($"Error connecting to serial port {_portName}.");
            }

            _sensorData = new();
            _sensorData.Values = new();
        }

        private bool Connect()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    return true;
                }

                _serialPort.Open();
                Thread.Sleep(2000); // Give the Arduino time to reset and initialize

                // First connection attempt
                _serialPort.WriteLine(IdentificationQuery);
                string response = _serialPort.ReadLine().Trim();
                Console.WriteLine($"Response to '{IdentificationQuery}': {response}");
                if (response.Contains(_identificationString))
                {
                    Console.WriteLine($"Arduino found on port {_portName}.");
                    return true;
                }

                // Second connection attempt, in case the first one fails
                Thread.Sleep(1000);
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _serialPort.WriteLine(IdentificationQuery);
                response = _serialPort.ReadLine().Trim();
                Console.WriteLine($"Response to '{IdentificationQuery}': {response}");
                if (response.Contains(_identificationString))
                {
                    Console.WriteLine($"Arduino found on port {_portName}.");
                    return true;
                }

                _serialPort.Close();
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing serial port {_portName}: {ex.Message}");
                return false;
            }
        }

        public SensorData GetSensorData()
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                if (!Connect())
                {
                    Console.WriteLine($"Serial connection to {_portName} is not active. Skipping data retrieval.");
                    return null;
                }
            }

            try
            {
                _serialPort.Write(_queryCommand);
                _bytesRead = _serialPort.Read(_receivedBuffer, 0, _receivedBuffer.Length);

                if (_bytesRead == _receivedBuffer.Length)
                {
                    for (int i = 0; i < _unpackedData.Length; i++)
                    {
                        _unpackedData[i] = BitConverter.ToSingle(_receivedBuffer, i * _floatSize);
                    }

                    _sensorData.Values["Water Temperature (°C)"] = _unpackedData[0];
                    _sensorData.Values["Case Temperature (°C)"] = _unpackedData[1];
                    _sensorData.Values["Ambient Temperature (°C)"] = _unpackedData[2];
                    _sensorData.Values["Water MV"] = _unpackedData[3];
                    _sensorData.Values["Case MV"] = _unpackedData[5];
                    _sensorData.Values["Radiator Fan Speed (%)"] = _unpackedData[6];
                    _sensorData.Values["Case Fan Speed (%)"] = _unpackedData[8];

                    return _sensorData;
                }
                else
                {
                    Console.WriteLine($"Error: Received bytes ({_bytesRead}) do not match the expected count ({_receivedBuffer.Length}).");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading from serial port {_portName}: {ex.Message}");
                // Attempt to restore the connection next time
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
                return null;
            }
        }

        public void Dispose()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
        }
    }
}