namespace HwMonLinux
{
    public interface ISensorDataProvider
    {
        string Name { get; }
        string FriendlyName { get; }
        bool GetSensorData(out (string, float)[] data);
    }
}