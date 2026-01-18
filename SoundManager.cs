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

        // フォルダ名は "Sounds"
        public const string FILE_OKI = "Sounds/oki.wav";
        public const string FILE_TORI = "Sounds/tori.wav";
        public const string FILE_YOBI1 = "Sounds/yobi1.wav";
        public const string FILE_YOBI2 = "Sounds/yobi2.wav";

        public void SetOutputDevice(string deviceId)
        {
            _deviceId = deviceId;
        }

        public void Play(string fileName, bool loop = false)
        {
            // 前の音を止める
            try { Stop(); } catch { }

            if (!File.Exists(fileName)) return;

            try
            {
                // デバイス選択ロジック（WaveOutEvent or WasapiOut）
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
                    // ★修正: ループ時は「1000ミリ秒(1秒)」の無音を入れる設定にする
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

    // ★修正: 無音時間を追加できるループストリーム
    public class LoopStream : WaveStream
    {
        WaveStream sourceStream;

        // 無音処理用の変数
        private long _delayBytes;      // 1秒分のバイト数
        private long _delayBytesRead;  // 現在読んだ無音バイト数
        private bool _inDelay;         // 今「無音モード」かどうか

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="sourceStream">元になる音声ストリーム</param>
        /// <param name="delayMilliseconds">ループ間の無音時間(ミリ秒)</param>
        public LoopStream(WaveStream sourceStream, int delayMilliseconds = 0)
        {
            this.sourceStream = sourceStream;
            this.EnableLooping = true;

            // 1秒あたりのデータ量(平均バイト数) × 秒数 ＝ 必要な無音データのバイト数
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
            set
            {
                sourceStream.Position = value;
                _inDelay = false;       // 位置が手動で変えられたら無音モード解除
                _delayBytesRead = 0;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;

            while (totalBytesRead < count)
            {
                if (!_inDelay)
                {
                    // --- 通常モード: 音声データを読む ---
                    int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);

                    if (bytesRead == 0)
                    {
                        // データが終わった場合
                        if (sourceStream.Position == 0 || !EnableLooping)
                        {
                            // ループしない設定ならここで終了
                            break;
                        }

                        // ループする場合、ここから「無音モード」に切り替え
                        _inDelay = true;
                        _delayBytesRead = 0;
                        continue; // whileループの先頭に戻って、今度は無音データを読みに行く
                    }
                    totalBytesRead += bytesRead;
                }
                else
                {
                    // --- 無音モード: 0 (無音) を書き込む ---
                    // 今回読み取るべきバイト数（バッファの残り容量 vs 無音の残り時間）
                    long bytesToRead = Math.Min(count - totalBytesRead, _delayBytes - _delayBytesRead);

                    // バッファを0で埋める
                    Array.Clear(buffer, offset + totalBytesRead, (int)bytesToRead);

                    _delayBytesRead += bytesToRead;
                    totalBytesRead += (int)bytesToRead;

                    // 指定時間の無音が終わったら、曲の先頭に戻す
                    if (_delayBytesRead >= _delayBytes)
                    {
                        _inDelay = false;
                        sourceStream.Position = 0;
                    }
                }
            }
            return totalBytesRead;
        }
    }
}