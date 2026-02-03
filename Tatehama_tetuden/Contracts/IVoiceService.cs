namespace RailwayPhone;

public interface IVoiceService : IDisposable
{
    bool IsMuted { get; set; }

    void StartTransmission(string myId, string targetId, string serverIp, int serverPort, int inputDevId, int outputDevId);
    void StopTransmission();
    void ChangeOutputDevice(int outputDeviceId);
}
