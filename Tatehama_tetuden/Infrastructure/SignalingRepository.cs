using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Tatehama_tetuden.Helpers;
using Tatehama_tetuden.Repositories;

namespace Tatehama_tetuden.Infrastructure;

public class SignalingRepository : ISignalingRepository
{
    private readonly ILogger<SignalingRepository> _logger;
    private readonly IAuthenticationRepository _auth;
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

    public SignalingRepository(ILogger<SignalingRepository> logger, IAuthenticationRepository auth)
    {
        _logger = logger;
        _auth = auth;
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task<bool> ConnectAsync()
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected) return true;
        try
        {
            // トークンを内部で取得
            string? accessToken = _auth.GetAccessToken();
            if (accessToken == null)
            {
                bool refreshed = await _auth.RefreshTokenAsync();
                if (refreshed) accessToken = _auth.GetAccessToken();
            }

            var url = $"{ServerAddress.SignalAddress}/hub/phone";

            if (_hubConnection != null) await _hubConnection.DisposeAsync();

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
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
        catch (Exception ex) { _logger.LogWarning(ex, "SignalR接続失敗、再接続を試行します"); RetryConnectionLoop().FireAndForget(_logger, "RetryConnectionLoop"); return false; }
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
        _hubConnection!.On<string>("ReceiveLoginSuccess", (myConnectionId) =>
        {
            try { LoginSuccess?.Invoke(myConnectionId); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveLoginSuccess処理中にエラー発生"); }
        });

        _hubConnection!.On<string, string>("ReceiveIncoming", (fromNumber, callerConnectionId) =>
        {
            try { IncomingCallReceived?.Invoke(fromNumber, callerConnectionId); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveIncoming処理中にエラー発生"); }
        });

        _hubConnection!.On<string>("ReceiveAnswered", (responderId) =>
        {
            try { AnswerReceived?.Invoke(responderId); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveAnswered処理中にエラー発生"); }
        });

        _hubConnection!.On<string>("ReceiveCancel", (callerConnectionId) =>
        {
            try { CancelReceived?.Invoke(callerConnectionId); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveCancel処理中にエラー発生"); }
        });

        _hubConnection!.On<string>("ReceiveReject", (fromId) =>
        {
            try { RejectReceived?.Invoke(fromId); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveReject処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveBusy", () =>
        {
            try { BusyReceived?.Invoke(); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveBusy処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveHoldRequest", () =>
        {
            try { HoldReceived?.Invoke(); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveHoldRequest処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveResumeRequest", () =>
        {
            try { ResumeReceived?.Invoke(); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveResumeRequest処理中にエラー発生"); }
        });

        _hubConnection!.On<string>("ReceiveHangup", (fromId) =>
        {
            try { HangupReceived?.Invoke(fromId); }
            catch (Exception ex) { _logger.LogError(ex, "ReceiveHangup処理中にエラー発生"); }
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
