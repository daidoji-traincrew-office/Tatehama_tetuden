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

        public event Action<string, string, int> IncomingCallReceived;
        public event Action<string, int> AnswerReceived;

        // ★修正: 引数(fromNumber)を追加
        public event Action<string> HangupReceived;

        public event Action BusyReceived;
        public event Action HoldReceived;
        public event Action ResumeReceived;
        public event Action<string> MessageReceived;
        public event Action ConnectionLost;
        public event Action Reconnected;

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public async Task<bool> Connect(string ipAddress, int port)
        {
            try
            {
                string url = $"http://{ipAddress}:{port}/phoneHub";
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.On<string>("ReceiveMessage", (json) =>
                {
                    MessageReceived?.Invoke(json);
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
                                case "INCOMING":
                                    IncomingCallReceived?.Invoke(Get("from"), Get("from_ip"), GetInt("udp_port"));
                                    break;
                                case "ANSWERED":
                                    AnswerReceived?.Invoke(Get("from_ip"), GetInt("udp_port"));
                                    break;
                                case "HANGUP":
                                    // ★修正: fromを取得して渡す
                                    HangupReceived?.Invoke(Get("from"));
                                    break;
                                case "BUSY":
                                    BusyReceived?.Invoke();
                                    break;
                                case "HOLD_REQUEST":
                                    HoldReceived?.Invoke();
                                    break;
                                case "RESUME_REQUEST":
                                    ResumeReceived?.Invoke();
                                    break;
                                case "CANCEL":
                                    // CANCELの場合は発信者指定はないが、現在の処理をキャンセルさせる
                                    HangupReceived?.Invoke("");
                                    break;
                            }
                        }
                    }
                    catch { }
                });

                _hubConnection.Closed += (e) => { ConnectionLost?.Invoke(); return Task.CompletedTask; };
                _hubConnection.Reconnected += (s) => { Reconnected?.Invoke(); return Task.CompletedTask; };

                await _hubConnection.StartAsync();
                return true;
            }
            catch { return false; }
        }

        // --- 送信メソッド (変更なし) ---
        public async void SendLogin(string myNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Login", myNumber); } catch { } }
        public async void SendCall(string targetNumber, int udpPort) { if (IsConnected) try { await _hubConnection.InvokeAsync("Call", targetNumber, udpPort.ToString()); } catch { } }
        public async void SendAnswer(string targetNumber, int udpPort) { if (IsConnected) try { await _hubConnection.InvokeAsync("Answer", targetNumber, udpPort.ToString()); } catch { } }
        public async void SendHangup(string targetNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Hangup", targetNumber); } catch { } }
        public async void SendBusy(string callerNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Busy", callerNumber); } catch { } }
        public async void SendHold(string targetNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Hold", targetNumber); } catch { } }
        public async void SendResume(string targetNumber) { if (IsConnected) try { await _hubConnection.InvokeAsync("Resume", targetNumber); } catch { } }
        public async void Dispose() { if (_hubConnection != null) await _hubConnection.DisposeAsync(); }
    }
}