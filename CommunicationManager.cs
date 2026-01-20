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
        private bool _isManuallyDisconnecting = false; // 手動切断フラグ

        // イベント
        public event Action<string, string, int> IncomingCallReceived;
        public event Action<string, int> AnswerReceived;
        public event Action<string> HangupReceived;
        public event Action BusyReceived;
        public event Action HoldReceived;
        public event Action ResumeReceived;

        // 接続状態通知イベント
        public event Action ConnectionLost;      // 完全に切れた
        public event Action Reconnecting;        // 再接続を試みている
        public event Action Reconnected;         // 復帰した

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public async Task<bool> Connect(string ipAddress, int port)
        {
            // すでに接続済みなら何もしない
            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected) return true;

            try
            {
                string url = $"http://{ipAddress}:{port}/phoneHub";

                // インスタンス再利用防止のため作り直す
                if (_hubConnection != null) await _hubConnection.DisposeAsync();

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    // SignalR標準の自動再接続（0秒, 2秒, 10秒, 30秒待機してリトライ）
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                    .Build();

                RegisterHandlers();

                // ライフサイクルイベント
                _hubConnection.Reconnecting += (ex) => { Reconnecting?.Invoke(); return Task.CompletedTask; };
                _hubConnection.Reconnected += (id) => { Reconnected?.Invoke(); return Task.CompletedTask; };

                // ★重要: 自動再接続が諦めた場合(Closed)に、自力で無限リトライする
                _hubConnection.Closed += async (ex) =>
                {
                    ConnectionLost?.Invoke();
                    if (!_isManuallyDisconnecting)
                    {
                        await RetryConnectionLoop();
                    }
                };

                await _hubConnection.StartAsync();
                return true;
            }
            catch
            {
                // 初回接続失敗時もリトライループへ
                _ = RetryConnectionLoop();
                return false;
            }
        }

        // ★無限再接続ループ
        private async Task RetryConnectionLoop()
        {
            while (!_isManuallyDisconnecting && (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected))
            {
                Reconnecting?.Invoke(); // UIを「接続中...」にする
                try
                {
                    await Task.Delay(5000); // 5秒おきにトライ
                    await _hubConnection.StartAsync();
                    Reconnected?.Invoke();  // 成功したら復帰イベント
                    return;
                }
                catch { }
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
                        int GetInt(string k) => data.ContainsKey(k) && int.TryParse(data[k], out int v) ? v : 0;

                        switch (type)
                        {
                            case "INCOMING": IncomingCallReceived?.Invoke(Get("from"), Get("from_ip"), GetInt("udp_port")); break;
                            case "ANSWERED": AnswerReceived?.Invoke(Get("from_ip"), GetInt("udp_port")); break;
                            case "HANGUP": HangupReceived?.Invoke(Get("from")); break;
                            case "BUSY": BusyReceived?.Invoke(); break;
                            case "HOLD_REQUEST": HoldReceived?.Invoke(); break;
                            case "RESUME_REQUEST": ResumeReceived?.Invoke(); break;
                            case "CANCEL": HangupReceived?.Invoke(""); break;
                        }
                    }
                }
                catch { }
            });
        }

        // 送信メソッド群（変更なし）
        public async void SendLogin(string myNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Login", myNumber); } catch { } }
        public async void SendCall(string targetNumber, int udpPort) { if (IsConnected) try { await _hubConnection.InvokeAsync("Call", targetNumber, udpPort.ToString()); } catch { } }
        public async void SendAnswer(string targetNumber, int udpPort) { if (IsConnected) try { await _hubConnection.InvokeAsync("Answer", targetNumber, udpPort.ToString()); } catch { } }
        public async void SendHangup(string targetNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Hangup", targetNumber); } catch { } }
        public async void SendBusy(string callerNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Busy", callerNumber); } catch { } }
        public async void SendHold(string targetNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Hold", targetNumber); } catch { } }
        public async void SendResume(string targetNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Resume", targetNumber); } catch { } }

        public async void Dispose()
        {
            _isManuallyDisconnecting = true;
            if (_hubConnection != null) await _hubConnection.DisposeAsync();
        }
    }
}