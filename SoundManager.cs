using System;
using System.IO;
using NAudio.Wave;

namespace RailwayPhone
{
    public class SoundManager : IDisposable
    {
        // 音声ファイル名定義
        public const string FILE_YOBI1 = "yobi1.wav";
        public const string FILE_YOBI2 = "yobi2.wav";
        public const string FILE_YOBIDASHI = "yobidashi.wav";
        public const string FILE_TORI = "tori.wav";
        public const string FILE_OKI = "oki.wav";
        public const string FILE_HOLD1 = "hold1.wav";
        public const string FILE_HOLD2 = "hold2.wav";

        // ★ここが重要: 話し中音の定義
        public const string FILE_WATYU = "watyu.wav";

        private IWavePlayer _outputDevice;
        private AudioFileReader _audioFile;
        private int _currentDeviceId = -1;

        public void SetOutputDevice(string deviceIdStr)
        {
            if (int.TryParse(deviceIdStr, out int id)) _currentDeviceId = id;
            else _currentDeviceId = -1;
        }

        public void Play(string fileName, bool loop = false)
        {
            Stop();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", fileName);
            if (!File.Exists(path)) return;

            try
            {
                _audioFile = new AudioFileReader(path);

                if (loop)
                {
                    var loopStream = new LoopStream(_audioFile);
                    _outputDevice = new WaveOutEvent { DeviceNumber = _currentDeviceId };
                    _outputDevice.Init(loopStream);
                }
                else
                {
                    _outputDevice = new WaveOutEvent { DeviceNumber = _currentDeviceId };
                    _outputDevice.Init(_audioFile);
                }

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
    }

    // ループ再生用ヘルパー
    public class LoopStream : WaveStream
    {
        private WaveStream sourceStream;
        public LoopStream(WaveStream sourceStream) { this.sourceStream = sourceStream; this.EnableLooping = true; }
        public bool EnableLooping { get; set; }
        public override WaveFormat WaveFormat => sourceStream.WaveFormat;
        public override long Length => sourceStream.Length;
        public override long Position { get => sourceStream.Position; set => sourceStream.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    if (sourceStream.Position == 0 || !EnableLooping) break;
                    sourceStream.Position = 0;
                }
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }
    }
}