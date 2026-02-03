using System;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using NAudio.Wave;
using NAudio.Codecs;
using RailwayPhone.Protos;

namespace RailwayPhone;

public class VoiceService : IVoiceService
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
    private bool _isActive = false;

    public bool IsMuted { get; set; } = false;

    public async void StartTransmission(string myId, string targetId, string serverIp, int serverPort, int inputDevId, int outputDevId)
    {
        if (_isActive) StopTransmission();

        _myId = myId;
        _targetId = targetId;

        InitAudio(inputDevId, outputDevId);

        try
        {
            string url = $"http://{serverIp}:{serverPort}";

            _channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                }
            });

            _client = new VoiceRelay.VoiceRelayClient(_channel);

            _call = _client.JoinSession();
            _isActive = true;

            _ = Task.Run(ReceiveLoop);

            _waveIn!.StartRecording();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"gRPC Start Error: {ex.Message}");
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
        catch { }
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
        catch { }
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
            System.Diagnostics.Debug.WriteLine($"gRPC Recv Error: {ex.Message}");
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

    public void StopTransmission()
    {
        _isActive = false;
        try
        {
            _call?.RequestStream.CompleteAsync();
            _call?.Dispose();
            _channel?.Dispose();
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveOut?.Stop();
            _waveOut?.Dispose();
        }
        catch { }
    }

    public void Dispose()
    {
        StopTransmission();
    }
}
