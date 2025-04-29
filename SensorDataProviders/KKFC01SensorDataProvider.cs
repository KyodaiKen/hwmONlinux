using System;
using System.Collections.Generic;
using System.IO.Ports;
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

        public string Name => "fan.controller.KKFC01";
        public string FriendlyName { get; }

        private readonly List<string> _provideSensors;

        private readonly string[] _sensorNames = {
            "temp.water",
            "temp.case",
            "temp.ambient",
            "matrix.radiator-fans",
            "matrix.case-fans",
            "matrix.unused",
            "pwm.radiator-fans",
            "pwm.case-fans",
            "pwm.unused"
        };

        //Persistent memory to avoid reallocations
        private byte[] _receivedBuffer;
        private int _bytesRead;
        private float[] _unpackedData;
        private (string, float)[] _sensorData;

        public KKFC01SensorDataProvider(string friendlyName, string portName, int baudRate, string queryCommand, string identificationString, List<string> provideSensors)
        {
            FriendlyName = friendlyName;
            _portName = portName;
            _baudRate = baudRate;
            _queryCommand = queryCommand;
            _identificationString = identificationString;

            _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One);
            _serialPort.ReadTimeout = SerialTimeoutMs;
            _serialPort.WriteTimeout = SerialTimeoutMs;

            _provideSensors = provideSensors;

            _receivedBuffer = new byte[_floatSize * _numEntries * 3];
            _unpackedData = new float[_numEntries * 3];

            if (!Connect())
            {
                Console.WriteLine($"Error connecting to serial port {_portName}.");
            }

            _sensorData = new (string, float)[_provideSensors.Count];
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

        public bool GetSensorData(out (string, float)[] data)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                if (!Connect())
                {
                    Console.WriteLine($"Serial connection to {_portName} is not active. Skipping data retrieval.");
                    data = Array.Empty<(string, float)>();
                    return false;
                }
            }

            try
            {
                _serialPort.Write(_queryCommand);
                _bytesRead = _serialPort.Read(_receivedBuffer, 0, _receivedBuffer.Length);

                if (_bytesRead == _receivedBuffer.Length)
                {
                    int si = 0;
                    for (int i = 0; i < _unpackedData.Length; i++)
                    {
                        if (_provideSensors.Contains(_sensorNames[i]))
                        {
                            _unpackedData[i] = BitConverter.ToSingle(_receivedBuffer, i * _floatSize);
                            _sensorData[si].Item1 = _sensorNames[i];
                            _sensorData[si].Item2 = _unpackedData[i];
                            si++;
                        }
                    }

                    data = _sensorData;
                    return true;
                }
                else
                {
                    Console.WriteLine($"Error: Received bytes ({_bytesRead}) do not match the expected count ({_receivedBuffer.Length}).");
                    data = Array.Empty<(string, float)>();
                    return false;
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
                data = Array.Empty<(string, float)>();
                return false;
            }
        }

        public void Dispose()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _sensorData = null;
                _unpackedData = null;
                _receivedBuffer = null;
                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
        }
    }
}