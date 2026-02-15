namespace Tatehama_tetuden.Contracts;

public interface IVoiceService : IDisposable
{
    bool IsMuted { get; set; }

    void StartTransmission(string myId, string targetId, int inputDevId, int outputDevId, string? accessToken = null);
    Task StopTransmission();
    void ChangeOutputDevice(int outputDeviceId);
}
