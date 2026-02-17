using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using NAudio.Codecs;
using NAudio.Wave;
using RailwayPhone.Protos;
using Tatehama_tetuden.Repositories;

namespace Tatehama_tetuden.Infrastructure;

public class VoiceRepository(ILogger<VoiceRepository> logger, IAuthenticationRepository auth)
    : IVoiceRepository
{
    private readonly WaveFormat _format = new WaveFormat(8000, 16, 1);

    private GrpcChannel? _channel;
    private VoiceRelay.VoiceRelayClient? _client;
    private AsyncDuplexStreamingCall<VoiceData, VoiceData>? _call;

    private WaveInEvent? _waveIn;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _waveProvider;

    private string? _myId;
    private string? _targetId;
    private volatile bool _isActive = false;

    public bool IsMuted { get; set; } = false;

    public async Task StartTransmission(string myId, string targetId, int inputDevId, int outputDevId)
    {
        if (_isActive) await StopTransmission();

        // トークンを内部で取得
        string? accessToken = auth.GetAccessToken();

        _myId = myId;
        _targetId = targetId;

        InitAudio(inputDevId, outputDevId);

        try
        {
            string url = ServerAddress.GrpcAddress;

            _channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                }
            });

            _client = new VoiceRelay.VoiceRelayClient(_channel);

            // Metadata でトークンを送信
            var metadata = new Metadata();
            if (!string.IsNullOrEmpty(accessToken))
            {
                metadata.Add("authorization", $"Bearer {accessToken}");
            }

            var callOptions = new CallOptions(headers: metadata);
            _call = _client.JoinSession(callOptions);
            _isActive = true;

            _ = Task.Run(async () =>
            {
                try { await ReceiveLoop(); }
                catch (Exception ex) { logger.LogError(ex, "ReceiveLoop中にエラー"); }
            });

            _waveIn!.StartRecording();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "gRPC音声通信の開始に失敗");
        }
    }

    private void InitAudio(int inId, int outId)
    {
        _waveIn = new WaveInEvent { BufferMilliseconds = 50, WaveFormat = _format };
        if (inId != -1) _waveIn.DeviceNumber = inId;
        _waveIn.DataAvailable += OnAudioCaptured;

        _waveProvider = new BufferedWaveProvider(_format) { DiscardOnBufferOverflow = true };

        InitWaveOut(outId);
    }

    private void InitWaveOut(int deviceId)
    {
        try
        {
            _waveOut = new WaveOutEvent();
            if (deviceId != -1) _waveOut.DeviceNumber = deviceId;
            _waveOut.Init(_waveProvider!);
            _waveOut.Play();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "音声出力デバイス初期化失敗 (deviceId={DeviceId})", deviceId);
        }
    }

    private async void OnAudioCaptured(object sender, WaveInEventArgs e)
    {
        if (!_isActive || IsMuted || _call == null) return;

        try
        {
            byte[] encoded = new byte[e.BytesRecorded / 2];
            int outIndex = 0;
            for (int n = 0; n < e.BytesRecorded; n += 2)
            {
                short sample = (short)((e.Buffer[n + 1] << 8) | e.Buffer[n]);
                encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(sample);
            }

            await _call.RequestStream.WriteAsync(new VoiceData
            {
                ClientId = _myId!,
                TargetId = _targetId!,
                AudioContent = Google.Protobuf.ByteString.CopyFrom(encoded)
            });
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "音声データ送信エラー");
        }
    }

    private async Task ReceiveLoop()
    {
        try
        {
            if (_call == null) return;

            await foreach (var data in _call.ResponseStream.ReadAllAsync())
            {
                if (!_isActive) break;

                byte[] received = data.AudioContent.ToByteArray();
                byte[] decoded = new byte[received.Length * 2];
                int outIndex = 0;

                for (int n = 0; n < received.Length; n++)
                {
                    short sample = MuLawDecoder.MuLawToLinearSample(received[n]);
                    decoded[outIndex++] = (byte)(sample & 0xFF);
                    decoded[outIndex++] = (byte)(sample >> 8);
                }

                _waveProvider!.AddSamples(decoded, 0, decoded.Length);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "gRPC音声受信エラー");
        }
    }

    public void ChangeOutputDevice(int outputDeviceId)
    {
        if (!_isActive) return;
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        InitWaveOut(outputDeviceId);
    }

    public async Task StopTransmission()
    {
        _isActive = false;
        try
        {
            if (_call != null)
                await _call.RequestStream.CompleteAsync();
            _call?.Dispose();
            _channel?.Dispose();
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveOut?.Stop();
            _waveOut?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "音声通信クリーンアップエラー");
        }
    }

    public void Dispose()
    {
        StopTransmission().GetAwaiter().GetResult();
    }
}
