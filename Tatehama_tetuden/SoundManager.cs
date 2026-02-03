using System;
using System.IO;
using NAudio.Wave;

namespace RailwayPhone
{
    /// <summary>
    /// 効果音（着信音、呼出音、保留音など）の再生管理を行うクラス。
    /// 通常再生に加え、ループ再生や間隔付きループ再生をサポートします。
    /// </summary>
    public class SoundManager : IDisposable
    {
        #region 音声ファイル定数

        public const string FILE_YOBI1 = "yobi1.wav";       // 一般着信音
        public const string FILE_YOBI2 = "yobi2.wav";       // 司令着信音
        public const string FILE_YOBIDASHI = "yobidashi.wav"; // 呼出音 (プルルル...)
        public const string FILE_TORI = "tori.wav";         // 受話器を取る音
        public const string FILE_OKI = "oki.wav";           // 受話器を置く音
        public const string FILE_HOLD1 = "hold1.wav";       // 保留音1
        public const string FILE_HOLD2 = "hold2.wav";       // 保留音2
        public const string FILE_WATYU = "watyu.wav";       // 話し中音 (プー、プー...)

        #endregion

        #region フィールド

        // 出力デバイス (スピーカー)
        private IWavePlayer _outputDevice;

        // 読み込んだオーディオファイル
        private AudioFileReader _audioFile;

        // 選択された出力デバイスID (-1は既定のデバイス)
        private int _currentDeviceId = -1;

        #endregion

        #region 公開メソッド

        /// <summary>
        /// 再生に使用する出力デバイスを設定します。
        /// </summary>
        /// <param name="deviceIdStr">デバイスID文字列</param>
        public void SetOutputDevice(string deviceIdStr)
        {
            if (int.TryParse(deviceIdStr, out int id))
            {
                _currentDeviceId = id;
            }
            else
            {
                _currentDeviceId = -1; // パース失敗時は既定デバイスへ
            }
        }

        /// <summary>
        /// 指定された音声ファイルを再生します。
        /// </summary>
        /// <param name="fileName">再生するファイル名 (Soundsフォルダ内)</param>
        /// <param name="loop">ループ再生するかどうか</param>
        /// <param name="loopIntervalMs">ループ時の無音間隔 (ミリ秒)。0の場合は間隔なし。</param>
        public void Play(string fileName, bool loop = false, int loopIntervalMs = 0)
        {
            // 既存の再生を停止
            Stop();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", fileName);
            if (!File.Exists(path)) return;

            try
            {
                _audioFile = new AudioFileReader(path);
                WaveStream finalStream = _audioFile;

                // ループ設定の適用
                if (loop)
                {
                    if (loopIntervalMs > 0)
                    {
                        // 間隔付きループ (トゥルルル... [無音] ...トゥルルル)
                        finalStream = new IntervalLoopStream(_audioFile, loopIntervalMs);
                    }
                    else
                    {
                        // 通常ループ (BGMや保留音など)
                        finalStream = new LoopStream(_audioFile);
                    }
                }

                // デバイス初期化と再生開始
                _outputDevice = new WaveOutEvent { DeviceNumber = _currentDeviceId };
                _outputDevice.Init(finalStream);
                _outputDevice.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sound Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在再生中の音声を停止し、リソースを解放します。
        /// </summary>
        public void Stop()
        {
            _outputDevice?.Stop();
            _outputDevice?.Dispose();
            _outputDevice = null;

            _audioFile?.Dispose();
            _audioFile = null;
        }

        /// <summary>
        /// リソースを破棄します。
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        #endregion

        #region 内部クラス (カスタムWaveStream)

        /// <summary>
        /// 音声を単純にループ再生させるためのストリームクラス
        /// </summary>
        private class LoopStream : WaveStream
        {
            private WaveStream sourceStream;

            public LoopStream(WaveStream sourceStream)
            {
                this.sourceStream = sourceStream;
            }

            public override WaveFormat WaveFormat => sourceStream.WaveFormat;
            public override long Length => long.MaxValue; // 無限ループなので長さは最大値
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
                        // 最後まで読み込んだら先頭に戻す
                        if (sourceStream.Position == 0) break; // 空ファイル対策
                        sourceStream.Position = 0;
                    }
                    totalBytesRead += bytesRead;
                }
                return totalBytesRead;
            }
        }

        /// <summary>
        /// 音声の再生後に一定時間の無音（サイレンス）を挟んでループさせるストリームクラス
        /// (電話の呼び出し音などの表現に使用)
        /// </summary>
        private class IntervalLoopStream : WaveStream
        {
            private WaveStream sourceStream;
            private int silenceBytesTotal;   // 無音として書き込むべき総バイト数
            private int silenceBytesWritten; // すでに書き込んだ無音バイト数
            private bool inSilenceMode = false; // 現在無音モードかどうか

            public IntervalLoopStream(WaveStream sourceStream, int intervalMs)
            {
                this.sourceStream = sourceStream;

                // ミリ秒をバイト数に変換 ( 平均バイトレート * 秒数 )
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
                        // --- 無音データの生成モード ---
                        int needed = count - bytesWritten;
                        int remaining = silenceBytesTotal - silenceBytesWritten;
                        int toWrite = Math.Min(needed, remaining);

                        // バッファを0埋め（無音）
                        Array.Clear(buffer, offset + bytesWritten, toWrite);

                        bytesWritten += toWrite;
                        silenceBytesWritten += toWrite;

                        // 無音期間が終了したら、音声再生モードへ戻る
                        if (silenceBytesWritten >= silenceBytesTotal)
                        {
                            inSilenceMode = false;
                            silenceBytesWritten = 0;
                            sourceStream.Position = 0; // 音声を先頭へ巻き戻し
                        }
                    }
                    else
                    {
                        // --- 音声データの読み込みモード ---
                        int read = sourceStream.Read(buffer, offset + bytesWritten, count - bytesWritten);

                        if (read == 0)
                        {
                            // 音声が最後まで終わったら、無音モードへ移行
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

        #endregion
    }
}