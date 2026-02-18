namespace Tatehama_tetuden.Repositories;

public interface IVoiceRepository : IDisposable
{
    bool IsMuted { get; set; }

    Task StartTransmission(int inputDevId, int outputDevId);
    Task StopTransmission();
    void ChangeOutputDevice(int outputDeviceId);
}
