namespace Tatehama_tetuden.Repositories;

public interface IVoiceRepository : IDisposable
{
    bool IsMuted { get; set; }

    Task StartTransmission(string myId, string targetId, int inputDevId, int outputDevId);
    Task StopTransmission();
    void ChangeOutputDevice(int outputDeviceId);
}
