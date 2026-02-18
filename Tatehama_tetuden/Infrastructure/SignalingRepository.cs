using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Tatehama_tetuden.Models;
using Tatehama_tetuden.Repositories;

namespace Tatehama_tetuden.Infrastructure;

public class SignalingRepository(ILogger<SignalingRepository> logger, IAuthenticationRepository auth)
    : ISignalingRepository
{
    private HubConnection? _hubConnection;
    private bool _isManuallyDisconnecting = false;

    public event Action<string>?  IncomingCallReceived;
    public event Action?          AnswerReceived;
    public event Action?          HangupReceived;
    public event Action?          CancelReceived;
    public event Action?          RejectReceived;
    public event Action?          HoldReceived;
    public event Action?          ResumeReceived;
    public event Action?          ConnectionLost;
    public event Action?          Reconnecting;
    public event Action?          Reconnected;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task<bool> ConnectAsync()
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected) return true;
        try
        {
            // トークンを内部で取得
            string? accessToken = auth.GetAccessToken();
            if (accessToken == null)
            {
                bool refreshed = await auth.RefreshTokenAsync();
                if (refreshed) accessToken = auth.GetAccessToken();
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR接続失敗、再接続を試行します");
            _ = Task.Run(async () =>
            {
                try { await RetryConnectionLoop(); }
                catch (Exception retryEx) { logger.LogError(retryEx, "RetryConnectionLoop中にエラー"); }
            });
            return false;
        }
    }

    private async Task RetryConnectionLoop()
    {
        while (!_isManuallyDisconnecting && (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected))
        {
            Reconnecting?.Invoke();
            try { await Task.Delay(5000); await _hubConnection!.StartAsync(); Reconnected?.Invoke(); return; } catch (Exception ex) { logger.LogDebug(ex, "再接続試行失敗"); }
        }
    }

    private void RegisterHandlers()
    {
        _hubConnection!.On<string>("ReceiveIncoming", (fromNumber) =>
        {
            try { IncomingCallReceived?.Invoke(fromNumber); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveIncoming処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveAnswered", () =>
        {
            try { AnswerReceived?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveAnswered処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveCancel", () =>
        {
            try { CancelReceived?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveCancel処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveReject", () =>
        {
            try { RejectReceived?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveReject処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveHoldRequest", () =>
        {
            try { HoldReceived?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveHoldRequest処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveResumeRequest", () =>
        {
            try { ResumeReceived?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveResumeRequest処理中にエラー発生"); }
        });

        _hubConnection!.On("ReceiveHangup", () =>
        {
            try { HangupReceived?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "ReceiveHangup処理中にエラー発生"); }
        });
    }

    public async Task SendLogin(string myNumber)  { if (IsConnected) await _hubConnection!.InvokeAsync("Login", myNumber); }
    public async Task<CallResponse> SendCall(string targetNumber)
    {
        if (IsConnected) return await _hubConnection!.InvokeAsync<CallResponse>("Call", targetNumber);
        return new CallResponse(false);
    }
    public async Task SendAnswer()
    {
        if (IsConnected) await _hubConnection!.InvokeAsync("Answer");
    }
    public async Task SendReject()  { if (IsConnected) await _hubConnection!.InvokeAsync("Reject"); }
    public async Task SendHangup()  { if (IsConnected) await _hubConnection!.InvokeAsync("Hangup"); }
    public async Task SendHold()    { if (IsConnected) await _hubConnection!.InvokeAsync("Hold"); }
    public async Task SendResume()  { if (IsConnected) await _hubConnection!.InvokeAsync("Resume"); }

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
