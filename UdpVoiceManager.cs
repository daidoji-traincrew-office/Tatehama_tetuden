using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Codecs; // G.711圧縮用

namespace RailwayPhone
{
    public class UdpVoiceManager : IDisposable
    {
        // 固定電話品質: 8kHz, 16bit, Mono
        private readonly WaveFormat _format = new WaveFormat(8000, 16, 1);

        private UdpClient _udpClient;
        private WaveInEvent _waveIn;
        private WaveOutEvent _waveOut;
        private BufferedWaveProvider _waveProvider;

        private IPEndPoint _remoteEndPoint; // 通話相手のアドレス
        private bool _isActive = false;

        // 自分が待ち受けているポート番号
        public int LocalPort { get; private set; }

        public UdpVoiceManager()
        {
            // 受信用のUDPポートを自動割り当て(ポート0指定)で開く
            _udpClient = new UdpClient(0);
            LocalPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;

            // 受信ループを開始
            Task.Run(ReceiveLoop);
        }

        // 通話開始 (相手のIPとポートを指定)
        public void StartTransmission(string remoteIp, int remotePort, int inputDeviceId = -1, int outputDeviceId = -1)
        {
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);

            // --- マイク設定 ---
            _waveIn = new WaveInEvent();
            _waveIn.BufferMilliseconds = 50; // 遅延を減らすため短めに
            _waveIn.DeviceNumber = inputDeviceId; // 指定のデバイス
            _waveIn.WaveFormat = _format;
            _waveIn.DataAvailable += OnAudioCaptured;

            // --- スピーカー設定 ---
            _waveProvider = new BufferedWaveProvider(_format);
            _waveProvider.DiscardOnBufferOverflow = true; // 遅延蓄積防止

            _waveOut = new WaveOutEvent();
            _waveOut.DeviceNumber = outputDeviceId; // 指定のデバイス
            _waveOut.Init(_waveProvider);

            _isActive = true;
            _waveIn.StartRecording();
            _waveOut.Play();
        }

        // 通話終了
        public void StopTransmission()
        {
            _isActive = false;

            try
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;

                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;

                _waveProvider?.ClearBuffer();
            }
            catch { /* 無視 */ }
        }

        // マイクから音が入ったとき -> 圧縮して送信
        private void OnAudioCaptured(object sender, WaveInEventArgs e)
        {
            if (!_isActive || _remoteEndPoint == null) return;

            try
            {
                // 16bit PCM を G.711 mu-law (byte) に圧縮
                // データサイズが半分になります (通信量削減 & 電話っぽい音質)
                byte[] encoded = new byte[e.BytesRecorded / 2];
                int outIndex = 0;
                for (int n = 0; n < e.BytesRecorded; n += 2)
                {
                    // リトルエンディアンでshortに変換
                    short sample = (short)((e.Buffer[n + 1] << 8) | e.Buffer[n]);
                    // G.711圧縮
                    encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(sample);
                }

                // UDPで送信
                _udpClient.SendAsync(encoded, encoded.Length, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"マイク送信エラー: {ex.Message}");
            }
        }

        // UDPを受信し続けるループ
        private async Task ReceiveLoop()
        {
            while (true)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    if (!_isActive) continue;

                    byte[] receivedBytes = result.Buffer;

                    // G.711 mu-law を 16bit PCM に解凍
                    byte[] decoded = new byte[receivedBytes.Length * 2];
                    int outIndex = 0;
                    for (int n = 0; n < receivedBytes.Length; n++)
                    {
                        short sample = MuLawDecoder.MuLawToLinearSample(receivedBytes[n]);
                        decoded[outIndex++] = (byte)(sample & 0xFF);
                        decoded[outIndex++] = (byte)(sample >> 8);
                    }

                    // 再生バッファに追加
                    _waveProvider?.AddSamples(decoded, 0, decoded.Length);
                }
                catch (ObjectDisposedException)
                {
                    break; // 終了時
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"受信エラー: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            StopTransmission();
            _udpClient?.Close();
        }
    }
}