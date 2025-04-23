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

            if (!Connect())
            {
                Console.WriteLine($"Error connecting to serial port {_portName}.");
            }
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
                byte[] receivedBuffer = new byte[_floatSize * _numEntries * 3];
                int bytesRead = _serialPort.Read(receivedBuffer, 0, receivedBuffer.Length);

                if (bytesRead == receivedBuffer.Length)
                {
                    float[] unpackedData = new float[_numEntries * 3];
                    for (int i = 0; i < unpackedData.Length; i++)
                    {
                        unpackedData[i] = BitConverter.ToSingle(receivedBuffer, i * _floatSize);
                    }

                    return new SensorData
                    {
                        Values = new Dictionary<string, object>
                        {
                            { "Water Temperature (°C)", unpackedData[0] },
                            { "Case Temperature (°C)", unpackedData[1] },
                            { "Ambient Temperature (°C)", unpackedData[2] },
                            { "Water MV", unpackedData[3] },
                            { "Case MV", unpackedData[5] },
                            { "Radiator Fan Speed (%)", unpackedData[6] },
                            { "Case Fan Speed (%)", unpackedData[8] }
                        }
                    };
                }
                else
                {
                    Console.WriteLine($"Error: Received bytes ({bytesRead}) do not match the expected count ({receivedBuffer.Length}).");
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