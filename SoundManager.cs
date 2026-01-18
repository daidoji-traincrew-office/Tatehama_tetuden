using System;
using System.IO;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace RailwayPhone
{
    public class SoundManager : IDisposable
    {
        private IWavePlayer _player;
        private AudioFileReader _reader;
        private string _deviceId;

        // ★ファイル定義を更新
        public const string FILE_OKI = "Sounds/oki.wav";
        public const string FILE_TORI = "Sounds/tori.wav";
        public const string FILE_YOBI1 = "Sounds/yobi1.wav";       // 一般着信
        public const string FILE_YOBI2 = "Sounds/yobi2.wav";       // 司令着信
        public const string FILE_YOBIDASHI = "Sounds/yobidashi.wav"; // 発信呼出音
        public const string FILE_HOLD1 = "Sounds/hold1.wav";       // 保留音1
        public const string FILE_HOLD2 = "Sounds/hold2.wav";       // 保留音2

        public void SetOutputDevice(string deviceId)
        {
            _deviceId = deviceId;
        }

        public void Play(string fileName, bool loop = false)
        {
            try { Stop(); } catch { }

            if (!File.Exists(fileName)) return;

            try
            {
                if (string.IsNullOrEmpty(_deviceId))
                {
                    _player = new WaveOutEvent();
                }
                else
                {
                    try
                    {
                        var mmDevice = new MMDeviceEnumerator().GetDevice(_deviceId);
                        _player = new WasapiOut(mmDevice, AudioClientShareMode.Shared, true, 200);
                    }
                    catch
                    {
                        _player = new WaveOutEvent();
                    }
                }

                _reader = new AudioFileReader(fileName);

                if (loop)
                {
                    // 1秒の無音間隔付きループ
                    var loopStream = new LoopStream(_reader, 1000);
                    _player.Init(loopStream);
                }
                else
                {
                    _player.Init(_reader);
                }

                _player.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"音声再生エラー: {ex.Message}");
                try { Stop(); } catch { }
            }
        }

        public void Stop()
        {
            try
            {
                if (_player != null) { _player.Stop(); _player.Dispose(); _player = null; }
                if (_reader != null) { _reader.Dispose(); _reader = null; }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    // LoopStreamクラスは変更なし（前回のままでOKですが、セットで載せておきます）
    public class LoopStream : WaveStream
    {
        WaveStream sourceStream;
        private long _delayBytes;
        private long _delayBytesRead;
        private bool _inDelay;

        public LoopStream(WaveStream sourceStream, int delayMilliseconds = 0)
        {
            this.sourceStream = sourceStream;
            this.EnableLooping = true;
            _delayBytes = (sourceStream.WaveFormat.AverageBytesPerSecond * delayMilliseconds) / 1000;
            _delayBytesRead = 0;
            _inDelay = false;
        }

        public bool EnableLooping { get; set; }
        public override WaveFormat WaveFormat => sourceStream.WaveFormat;
        public override long Length => sourceStream.Length;
        public override long Position
        {
            get => sourceStream.Position;
            set { sourceStream.Position = value; _inDelay = false; _delayBytesRead = 0; }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                if (!_inDelay)
                {
                    int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        if (sourceStream.Position == 0 || !EnableLooping) break;
                        _inDelay = true; _delayBytesRead = 0; continue;
                    }
                    totalBytesRead += bytesRead;
                }
                else
                {
                    long bytesToRead = Math.Min(count - totalBytesRead, _delayBytes - _delayBytesRead);
                    Array.Clear(buffer, offset + totalBytesRead, (int)bytesToRead);
                    _delayBytesRead += bytesToRead; totalBytesRead += (int)bytesToRead;
                    if (_delayBytesRead >= _delayBytes) { _inDelay = false; sourceStream.Position = 0; }
                }
            }
            return totalBytesRead;
        }
    }
}