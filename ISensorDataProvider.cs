namespace HwMonLinux
{
    public interface ISensorDataProvider
    {
        string Name { get; }
        string FriendlyName { get; }
        SensorData GetSensorData();
    }
}