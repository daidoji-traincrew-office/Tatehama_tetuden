using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Tatehama_tetuden.Contracts;

namespace Tatehama_tetuden.Infrastructure;

public class SignalingService : ISignalingService
{
    private readonly ILogger<SignalingService> _logger;
    private HubConnection? _hubConnection;
    private bool _isManuallyDisconnecting = false;

    public event Action<string>?         LoginSuccess;
    public event Action<string, string>? IncomingCallReceived;
    public event Action<string>?         AnswerReceived;
    public event Action<string>?         HangupReceived;
    public event Action<string>?         CancelReceived;
    public event Action<string>?         RejectReceived;
    public event Action?                 BusyReceived;
    public event Action?                 HoldReceived;
    public event Action?                 ResumeReceived;
    public event Action?                 ConnectionLost;
    public event Action?                 Reconnecting;
    public event Action?                 Reconnected;

    public SignalingService(ILogger<SignalingService> logger)
    {
        _logger = logger;
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task<bool> ConnectAsync(string? accessToken = null)
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected) return true;
        try
        {
            var url = $"{ServerAddress.SignalAddress}/phoneHub";

            if (_hubConnection != null) await _hubConnection.DisposeAsync();

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    // トークンを Authorization ヘッダーで送信（クエリ文字列に載せない）
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHandlers();

            _hubConnection.Reconnecting += (ex) => { Reconnecting?.Invoke(); return Task.CompletedTask; };
            _hubConnection.Reconnected += (id) => { Reconnected?.Invoke(); return Task.CompletedTask; };
            _hubConnection.Closed += async (ex) => { ConnectionLost?.Invoke(); if (!_isManuallyDisconnecting) await RetryConnectionLoop(); };

            await _hubConnection.StartAsync();
            return true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SignalR接続失敗、再接続を試行します"); _ = RetryConnectionLoop(); return false; }
    }

    private async Task RetryConnectionLoop()
    {
        while (!_isManuallyDisconnecting && (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected))
        {
            Reconnecting?.Invoke();
            try { await Task.Delay(5000); await _hubConnection!.StartAsync(); Reconnected?.Invoke(); return; } catch (Exception ex) { _logger.LogDebug(ex, "再接続試行失敗"); }
        }
    }

    private void RegisterHandlers()
    {
        _hubConnection!.On<string>("ReceiveMessage", (json) =>
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null && data.ContainsKey("type"))
                {
                    string type = data["type"];
                    string Get(string k) => data.ContainsKey(k) ? data[k] : "";
                    string fromId = Get("from_id");
                    if (string.IsNullOrEmpty(fromId)) fromId = Get("caller_id");

                    switch (type)
                    {
                        case "LOGIN_SUCCESS": LoginSuccess?.Invoke(Get("my_id")); break;
                        case "INCOMING": IncomingCallReceived?.Invoke(Get("from"), Get("caller_id")); break;
                        case "ANSWERED": AnswerReceived?.Invoke(Get("responder_id")); break;
                        case "HANGUP": HangupReceived?.Invoke(fromId); break;
                        case "CANCEL": CancelReceived?.Invoke(fromId); break;
                        case "REJECT": RejectReceived?.Invoke(fromId); break;
                        case "BUSY": BusyReceived?.Invoke(); break;
                        case "HOLD_REQUEST": HoldReceived?.Invoke(); break;
                        case "RESUME_REQUEST": ResumeReceived?.Invoke(); break;
                    }
                }
            }
            catch (JsonException ex) { _logger.LogWarning(ex, "SignalRメッセージのJSON解析失敗"); }
            catch (Exception ex) { _logger.LogError(ex, "SignalRメッセージ処理中にエラー発生"); }
        });
    }

    public async Task SendLogin(string myNumber)  { if (IsConnected) await _hubConnection!.InvokeAsync("Login", myNumber); }
    public async Task SendCall(string targetNumber) { if (IsConnected) await _hubConnection!.InvokeAsync("Call", targetNumber); }
    public async Task SendAnswer(string targetNumber, string callerId) { if (IsConnected) await _hubConnection!.InvokeAsync("Answer", targetNumber, callerId); }
    public async Task SendReject(string callerId)   { if (IsConnected) await _hubConnection!.InvokeAsync("Reject", callerId); }
    public async Task SendHangup(string targetId)   { if (IsConnected) await _hubConnection!.InvokeAsync("Hangup", targetId); }
    public async Task SendBusy(string callerId)     { if (IsConnected) await _hubConnection!.InvokeAsync("Busy", callerId); }
    public async Task SendHold(string targetId)     { if (IsConnected) await _hubConnection!.InvokeAsync("Hold", targetId); }
    public async Task SendResume(string targetId)   { if (IsConnected) await _hubConnection!.InvokeAsync("Resume", targetId); }

    // 注意: UI スレッドから呼ばれるとデッドロックリスクあり。
    // 可能な限り DisposeAsync() を使うこと。
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _isManuallyDisconnecting = true;
        if (_hubConnection != null)
            await _hubConnection.DisposeAsync();
    }
}
