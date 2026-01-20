using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace RailwayPhone
{
    public class CommunicationManager : IDisposable
    {
        private HubConnection _hubConnection;
        private bool _isManuallyDisconnecting = false;

        public event Action<string, string> IncomingCallReceived;
        public event Action<string> AnswerReceived;
        public event Action<string> LoginSuccess;
        public event Action<string> HangupReceived;
        public event Action<string> CancelReceived;

        // ★ここが重要: 拒否信号イベント
        public event Action<string> RejectReceived;

        public event Action BusyReceived;
        public event Action HoldReceived;
        public event Action ResumeReceived;

        public event Action ConnectionLost;
        public event Action Reconnecting;
        public event Action Reconnected;

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public async Task<bool> Connect(string ipAddress, int port)
        {
            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected) return true;
            try
            {
                string url = $"http://{ipAddress}:{port}/phoneHub";
                if (_hubConnection != null) await _hubConnection.DisposeAsync();

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect()
                    .Build();

                RegisterHandlers();

                _hubConnection.Reconnecting += (ex) => { Reconnecting?.Invoke(); return Task.CompletedTask; };
                _hubConnection.Reconnected += (id) => { Reconnected?.Invoke(); return Task.CompletedTask; };
                _hubConnection.Closed += async (ex) => { ConnectionLost?.Invoke(); if (!_isManuallyDisconnecting) await RetryConnectionLoop(); };

                await _hubConnection.StartAsync();
                return true;
            }
            catch { _ = RetryConnectionLoop(); return false; }
        }

        private async Task RetryConnectionLoop()
        {
            while (!_isManuallyDisconnecting && (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected))
            {
                Reconnecting?.Invoke();
                try { await Task.Delay(5000); await _hubConnection.StartAsync(); Reconnected?.Invoke(); return; } catch { }
            }
        }

        private void RegisterHandlers()
        {
            _hubConnection.On<string>("ReceiveMessage", (json) =>
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
                            case "REJECT": RejectReceived?.Invoke(fromId); break; // ★ここ
                            case "BUSY": BusyReceived?.Invoke(); break;
                            case "HOLD_REQUEST": HoldReceived?.Invoke(); break;
                            case "RESUME_REQUEST": ResumeReceived?.Invoke(); break;
                        }
                    }
                }
                catch { }
            });
        }

        public async void SendLogin(string myNumber) { if (IsConnected) await _hubConnection.InvokeAsync("Login", myNumber); }
        public async void SendCall(string targetNumber) { if (IsConnected) await _hubConnection.InvokeAsync("Call", targetNumber); }
        public async void SendAnswer(string targetNumber, string callerId) { if (IsConnected) await _hubConnection.InvokeAsync("Answer", targetNumber, callerId); }

        // ★ここ: 拒否信号送信メソッド
        public async void SendReject(string callerId) { if (IsConnected) await _hubConnection.InvokeAsync("Reject", callerId); }

        public async void SendHangup(string targetId) { if (IsConnected) await _hubConnection.InvokeAsync("Hangup", targetId); }
        public async void SendBusy(string callerId) { if (IsConnected) await _hubConnection.InvokeAsync("Busy", callerId); }
        public async void SendHold(string targetId) { if (IsConnected) await _hubConnection.InvokeAsync("Hold", targetId); }
        public async void SendResume(string targetId) { if (IsConnected) await _hubConnection.InvokeAsync("Resume", targetId); }

        public async void Dispose()
        {
            _isManuallyDisconnecting = true;
            if (_hubConnection != null) await _hubConnection.DisposeAsync();
        }
    }
}