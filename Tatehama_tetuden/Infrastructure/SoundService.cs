using System;
using System.IO;
using NAudio.Wave;

namespace RailwayPhone;

public class SoundService : ISoundService
{
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _audioFile;
    private int _currentDeviceId = -1;

    public void SetOutputDevice(string? deviceIdStr)
    {
        if (deviceIdStr != null && int.TryParse(deviceIdStr, out int id))
            _currentDeviceId = id;
        else
            _currentDeviceId = -1;
    }

    public void Play(string soundName, bool loop = false, int loopIntervalMs = 0)
    {
        Stop();

        string fileName = soundName + ".wav";
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", fileName);
        if (!File.Exists(path)) return;

        try
        {
            _audioFile = new AudioFileReader(path);
            WaveStream finalStream = _audioFile;

            if (loop)
            {
                if (loopIntervalMs > 0)
                    finalStream = new IntervalLoopStream(_audioFile, loopIntervalMs);
                else
                    finalStream = new LoopStream(_audioFile);
            }

            _outputDevice = new WaveOutEvent { DeviceNumber = _currentDeviceId };
            _outputDevice.Init(finalStream);
            _outputDevice.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sound Error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _outputDevice = null;

        _audioFile?.Dispose();
        _audioFile = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private class LoopStream : WaveStream
    {
        private WaveStream sourceStream;

        public LoopStream(WaveStream sourceStream)
        {
            this.sourceStream = sourceStream;
        }

        public override WaveFormat WaveFormat => sourceStream.WaveFormat;
        public override long Length => long.MaxValue;
        public override long Position
        {
            get => sourceStream.Position;
            set => sourceStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;

            while (totalBytesRead < count)
            {
                int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);

                if (bytesRead == 0)
                {
                    if (sourceStream.Position == 0) break;
                    sourceStream.Position = 0;
                }
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }
    }

    private class IntervalLoopStream : WaveStream
    {
        private WaveStream sourceStream;
        private int silenceBytesTotal;
        private int silenceBytesWritten;
        private bool inSilenceMode = false;

        public IntervalLoopStream(WaveStream sourceStream, int intervalMs)
        {
            this.sourceStream = sourceStream;

            int bytesPerSec = sourceStream.WaveFormat.AverageBytesPerSecond;
            this.silenceBytesTotal = (int)((double)bytesPerSec * intervalMs / 1000.0);
        }

        public override WaveFormat WaveFormat => sourceStream.WaveFormat;
        public override long Length => long.MaxValue;
        public override long Position
        {
            get => sourceStream.Position;
            set => sourceStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesWritten = 0;

            while (bytesWritten < count)
            {
                if (inSilenceMode)
                {
                    int needed = count - bytesWritten;
                    int remaining = silenceBytesTotal - silenceBytesWritten;
                    int toWrite = Math.Min(needed, remaining);

                    Array.Clear(buffer, offset + bytesWritten, toWrite);

                    bytesWritten += toWrite;
                    silenceBytesWritten += toWrite;

                    if (silenceBytesWritten >= silenceBytesTotal)
                    {
                        inSilenceMode = false;
                        silenceBytesWritten = 0;
                        sourceStream.Position = 0;
                    }
                }
                else
                {
                    int read = sourceStream.Read(buffer, offset + bytesWritten, count - bytesWritten);

                    if (read == 0)
                    {
                        inSilenceMode = true;
                        silenceBytesWritten = 0;
                    }
                    else
                    {
                        bytesWritten += read;
                    }
                }
            }
            return bytesWritten;
        }
    }
}
