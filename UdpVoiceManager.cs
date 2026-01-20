using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Codecs;

namespace RailwayPhone
{
    /// <summary>
    /// UDPプロトコルを使用した音声通話（P2P）を管理するクラス。
    /// マイク入力のキャプチャ、MuLaw圧縮、UDP送信、および受信音声のデコードと再生を行います。
    /// ※将来的にWebRTCへ移行する場合、このクラス全体が置き換わる可能性があります。
    /// </summary>
    public class UdpVoiceManager : IDisposable
    {
        #region 音声フォーマット設定

        // 電話音質標準: 8kHz, 16bit, モノラル
        private readonly WaveFormat _format = new WaveFormat(8000, 16, 1);

        #endregion

        #region 通信フィールド

        private UdpClient _udpClient;
        private IPEndPoint _remoteEndPoint;

        /// <summary>受信に使用しているローカルポート番号</summary>
        public int LocalPort { get; private set; }

        #endregion

        #region オーディオデバイスフィールド

        private WaveInEvent _waveIn;          // マイク入力
        private WaveOutEvent _waveOut;        // スピーカー出力
        private BufferedWaveProvider _waveProvider; // 受信バッファ

        #endregion

        #region 状態管理

        private bool _isActive = false;

        /// <summary>ミュート状態（trueの場合、音声パケットを送信しません）</summary>
        public bool IsMuted { get; set; } = false;

        #endregion

        /// <summary>
        /// コンストラクタ。
        /// OSが割り当てる空きポートを使用してUDP待機を開始します。
        /// </summary>
        public UdpVoiceManager()
        {
            // ポート0を指定すると、OSが空いているポートを自動割り当てする
            _udpClient = new UdpClient(0);
            LocalPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;

            // 受信ループをバックグラウンドで開始
            Task.Run(ReceiveLoop);
        }

        #region 通話制御メソッド

        /// <summary>
        /// 音声の送受信を開始します。
        /// </summary>
        /// <param name="remoteIp">相手先のIPアドレス</param>
        /// <param name="remotePort">相手先のUDPポート番号</param>
        /// <param name="inputDeviceId">マイクデバイスID (-1は既定)</param>
        /// <param name="outputDeviceId">スピーカーデバイスID (-1は既定)</param>
        public void StartTransmission(string remoteIp, int remotePort, int inputDeviceId = -1, int outputDeviceId = -1)
        {
            try
            {
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);

                // --- マイク入力設定 ---
                _waveIn = new WaveInEvent();
                _waveIn.BufferMilliseconds = 50; // 低遅延設定 (50ms)
                if (inputDeviceId != -1)
                {
                    _waveIn.DeviceNumber = inputDeviceId;
                }
                _waveIn.WaveFormat = _format;
                _waveIn.DataAvailable += OnAudioCaptured;

                // --- スピーカー出力設定 ---
                _waveProvider = new BufferedWaveProvider(_format);
                _waveProvider.DiscardOnBufferOverflow = true; // 遅延蓄積防止（バッファあふれ時は捨てる）

                // 出力デバイス初期化
                InitWaveOut(outputDeviceId);

                // 開始
                _isActive = true;
                IsMuted = false;
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartTransmission Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 通話中にスピーカー出力先を動的に変更します。
        /// </summary>
        /// <param name="outputDeviceId">新しい出力デバイスID</param>
        public void ChangeOutputDevice(int outputDeviceId)
        {
            if (!_isActive) return;

            // 現在の再生を停止して破棄
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;

            // 新しいデバイスで再初期化
            InitWaveOut(outputDeviceId);
        }

        /// <summary>
        /// 音声送受信を停止し、オーディオリソースを解放します。
        /// </summary>
        public void StopTransmission()
        {
            _isActive = false;
            try
            {
                // マイク停止
                if (_waveIn != null)
                {
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                    _waveIn = null;
                }

                // スピーカー停止
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

                // バッファクリア
                _waveProvider?.ClearBuffer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StopTransmission Error: {ex.Message}");
            }
        }

        #endregion

        #region 内部ロジック (オーディオ・ネットワーク)

        /// <summary>
        /// WaveOutEvent (スピーカー) を指定デバイスIDで初期化します。
        /// </summary>
        private void InitWaveOut(int deviceId)
        {
            try
            {
                _waveOut = new WaveOutEvent();
                if (deviceId != -1)
                {
                    _waveOut.DeviceNumber = deviceId;
                }
                _waveOut.Init(_waveProvider);
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Speaker Init Error: {ex.Message}");
            }
        }

        /// <summary>
        /// マイクから音声データが入ってきた時の処理。
        /// 音声を圧縮(MuLaw)してUDP送信します。
        /// </summary>
        private void OnAudioCaptured(object sender, WaveInEventArgs e)
        {
            // 非アクティブ、送信先未定、ミュート中は送信しない
            if (!_isActive || _remoteEndPoint == null || IsMuted) return;

            try
            {
                // エンコード: 16bit Linear PCM -> 8bit MuLaw
                // (データサイズを半分に圧縮して帯域を節約)
                byte[] encoded = new byte[e.BytesRecorded / 2];
                int outIndex = 0;

                for (int n = 0; n < e.BytesRecorded; n += 2)
                {
                    // 2バイト(byte[])を1つのshort(16bit)に変換
                    short sample = (short)((e.Buffer[n + 1] << 8) | e.Buffer[n]);

                    // MuLaw変換
                    encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(sample);
                }

                // UDP送信
                _udpClient.SendAsync(encoded, encoded.Length, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio Send Error: {ex.Message}");
            }
        }

        /// <summary>
        /// UDPパケット受信ループ。
        /// 受信データを解凍(MuLaw -> PCM)して再生バッファに追加します。
        /// </summary>
        private async Task ReceiveLoop()
        {
            while (true)
            {
                try
                {
                    // パケット待機 (非同期)
                    var result = await _udpClient.ReceiveAsync();

                    if (!_isActive) continue;

                    byte[] receivedBytes = result.Buffer;

                    // デコード: 8bit MuLaw -> 16bit Linear PCM
                    // (サイズを2倍に展開)
                    byte[] decoded = new byte[receivedBytes.Length * 2];
                    int outIndex = 0;

                    for (int n = 0; n < receivedBytes.Length; n++)
                    {
                        short sample = MuLawDecoder.MuLawToLinearSample(receivedBytes[n]);

                        // shortを2バイト(byte[])に分解 (リトルエンディアン)
                        decoded[outIndex++] = (byte)(sample & 0xFF);
                        decoded[outIndex++] = (byte)(sample >> 8);
                    }

                    // 再生バッファに追加
                    _waveProvider?.AddSamples(decoded, 0, decoded.Length);
                }
                catch (ObjectDisposedException)
                {
                    // クライアント破棄時はループ終了
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UDP Receive Error: {ex.Message}");
                }
            }
        }

        #endregion

        #region IDisposable 実装

        public void Dispose()
        {
            StopTransmission();
            _udpClient?.Close();
            _udpClient?.Dispose();
        }

        #endregion
    }
}