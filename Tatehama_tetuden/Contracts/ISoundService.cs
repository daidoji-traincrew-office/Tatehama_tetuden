namespace RailwayPhone;

public interface ISoundService : IDisposable
{
    void Play(string soundName, bool loop = false, int loopIntervalMs = 0);
    void Stop();
    void SetOutputDevice(string? deviceIdStr);
}
