using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Codecs;

namespace RailwayPhone
{
    public class UdpVoiceManager : IDisposable
    {
        private readonly WaveFormat _format = new WaveFormat(8000, 16, 1);

        private UdpClient _udpClient;
        private WaveInEvent _waveIn;
        private WaveOutEvent _waveOut;
        private BufferedWaveProvider _waveProvider;

        private IPEndPoint _remoteEndPoint;
        private bool _isActive = false;

        // ミュート機能
        public bool IsMuted { get; set; } = false;

        public int LocalPort { get; private set; }

        public UdpVoiceManager()
        {
            _udpClient = new UdpClient(0);
            LocalPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;
            Task.Run(ReceiveLoop);
        }

        public void StartTransmission(string remoteIp, int remotePort, int inputDeviceId = -1, int outputDeviceId = -1)
        {
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);

            // マイク設定
            _waveIn = new WaveInEvent();
            _waveIn.BufferMilliseconds = 50;
            if (inputDeviceId != -1) _waveIn.DeviceNumber = inputDeviceId;
            _waveIn.WaveFormat = _format;
            _waveIn.DataAvailable += OnAudioCaptured;

            // スピーカー設定
            _waveProvider = new BufferedWaveProvider(_format);
            _waveProvider.DiscardOnBufferOverflow = true;

            InitWaveOut(outputDeviceId); // 出力初期化

            _isActive = true;
            IsMuted = false;
            _waveIn.StartRecording();
        }

        // ★追加: 出力デバイスを動的に変更する
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

        // WaveOutの初期化ロジックを分離
        private void InitWaveOut(int deviceId)
        {
            try
            {
                _waveOut = new WaveOutEvent();
                if (deviceId != -1) _waveOut.DeviceNumber = deviceId;
                _waveOut.Init(_waveProvider);
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Speaker Init Error: {ex.Message}");
            }
        }

        public void StopTransmission()
        {
            _isActive = false;
            try
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose(); _waveIn = null;
                _waveOut?.Stop();
                _waveOut?.Dispose(); _waveOut = null;
                _waveProvider?.ClearBuffer();
            }
            catch { }
        }

        private void OnAudioCaptured(object sender, WaveInEventArgs e)
        {
            if (!_isActive || _remoteEndPoint == null || IsMuted) return;

            try
            {
                byte[] encoded = new byte[e.BytesRecorded / 2];
                int outIndex = 0;
                for (int n = 0; n < e.BytesRecorded; n += 2)
                {
                    short sample = (short)((e.Buffer[n + 1] << 8) | e.Buffer[n]);
                    encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(sample);
                }
                _udpClient.SendAsync(encoded, encoded.Length, _remoteEndPoint);
            }
            catch { }
        }

        private async Task ReceiveLoop()
        {
            while (true)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    if (!_isActive) continue;

                    byte[] receivedBytes = result.Buffer;
                    byte[] decoded = new byte[receivedBytes.Length * 2];
                    int outIndex = 0;
                    for (int n = 0; n < receivedBytes.Length; n++)
                    {
                        short sample = MuLawDecoder.MuLawToLinearSample(receivedBytes[n]);
                        decoded[outIndex++] = (byte)(sample & 0xFF);
                        decoded[outIndex++] = (byte)(sample >> 8);
                    }
                    _waveProvider?.AddSamples(decoded, 0, decoded.Length);
                }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        public void Dispose()
        {
            StopTransmission();
            _udpClient?.Close();
        }
    }
}