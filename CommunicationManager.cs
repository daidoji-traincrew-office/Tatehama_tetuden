using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace RailwayPhone
{
    /// <summary>
    /// SignalRを使用したサーバーとの通信管理クラス。
    /// 接続の維持、再接続、および各イベントの送受信を行います。
    /// </summary>
    public class CommunicationManager : IDisposable
    {
        #region フィールド

        private HubConnection _hubConnection;

        // 手動で切断したかどうかのフラグ（これがないと終了時も再接続しようとしてしまう）
        private bool _isManuallyDisconnecting = false;

        #endregion

        #region 公開イベント (受信通知)

        /// <summary>着信時に発火 (発信者番号, IP, Port)</summary>
        public event Action<string, string, int> IncomingCallReceived;

        /// <summary>相手が応答した時に発火 (相手IP, Port)</summary>
        public event Action<string, int> AnswerReceived;

        /// <summary>切断信号を受信した時に発火 (切断者番号)</summary>
        public event Action<string> HangupReceived;

        /// <summary>話し中信号を受信した時に発火</summary>
        public event Action BusyReceived;

        /// <summary>保留信号を受信した時に発火</summary>
        public event Action HoldReceived;

        /// <summary>再開信号を受信した時に発火</summary>
        public event Action ResumeReceived;

        #endregion

        #region 公開イベント (接続状態)

        /// <summary>サーバーとの接続が完全に切れた時に発火</summary>
        public event Action ConnectionLost;

        /// <summary>再接続を試みている時に発火</summary>
        public event Action Reconnecting;

        /// <summary>サーバーへの再接続が成功した時に発火</summary>
        public event Action Reconnected;

        #endregion

        /// <summary>
        /// 現在サーバーと接続されているかどうか
        /// </summary>
        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        #region 接続・切断ロジック

        /// <summary>
        /// 指定されたサーバーへの接続を開始します。
        /// </summary>
        /// <param name="ipAddress">サーバーIPアドレス</param>
        /// <param name="port">サーバーポート番号</param>
        /// <returns>接続成功ならtrue</returns>
        public async Task<bool> Connect(string ipAddress, int port)
        {
            // すでに接続済みなら何もしない
            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                return true;

            try
            {
                string url = $"http://{ipAddress}:{port}/phoneHub";

                // インスタンス再利用防止のため、既存があれば破棄して作り直す
                if (_hubConnection != null) await _hubConnection.DisposeAsync();

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    // SignalR標準の自動再接続ポリシー
                    // (0秒, 2秒, 10秒, 30秒待機してリトライ。これ以降はClosedイベントへ)
                    .WithAutomaticReconnect(new[] {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30)
                    })
                    .Build();

                // 受信ハンドラの登録
                RegisterHandlers();

                // --- ライフサイクルイベントの登録 ---

                // 自動再接続中
                _hubConnection.Reconnecting += (ex) =>
                {
                    Reconnecting?.Invoke();
                    return Task.CompletedTask;
                };

                // 自動再接続成功
                _hubConnection.Reconnected += (id) =>
                {
                    Reconnected?.Invoke();
                    return Task.CompletedTask;
                };

                // 接続断 (自動再接続も諦めた場合)
                // ★ここから自前の無限リトライループに入る
                _hubConnection.Closed += async (ex) =>
                {
                    ConnectionLost?.Invoke();
                    if (!_isManuallyDisconnecting)
                    {
                        await RetryConnectionLoop();
                    }
                };

                // 接続開始
                await _hubConnection.StartAsync();
                return true;
            }
            catch
            {
                // 初回接続失敗時もリトライループへ移行
                _ = RetryConnectionLoop();
                return false;
            }
        }

        /// <summary>
        /// 接続が確立されるまで永遠に再試行を続けるループ処理
        /// </summary>
        private async Task RetryConnectionLoop()
        {
            // 手動切断でなく、かつ未接続の間はループ
            while (!_isManuallyDisconnecting &&
                   (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected))
            {
                Reconnecting?.Invoke(); // UIを「接続中...」にする
                try
                {
                    await Task.Delay(5000); // 5秒おきにトライ
                    await _hubConnection.StartAsync();

                    Reconnected?.Invoke();  // 成功したら復帰イベント
                    return; // ループを抜ける
                }
                catch
                {
                    // 失敗したら次のループへ
                }
            }
        }

        /// <summary>
        /// サーバーからのメッセージ受信ハンドラを登録します。
        /// </summary>
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

                        // データ取得用ヘルパー関数
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
                                // キャンセル信号は空文字のHangupとして扱う
                                HangupReceived?.Invoke("");
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON Parse Error: {ex.Message}");
                }
            });
        }

        #endregion

        #region 送信メソッド群

        // ※ IsConnectedチェックを行い、切断時は送信しない（エラー防止）

        public async void SendLogin(string myNumber)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Login", myNumber); } catch { }
        }

        public async void SendCall(string targetNumber, int udpPort)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Call", targetNumber, udpPort.ToString()); } catch { }
        }

        public async void SendAnswer(string targetNumber, int udpPort)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Answer", targetNumber, udpPort.ToString()); } catch { }
        }

        public async void SendHangup(string targetNumber)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Hangup", targetNumber); } catch { }
        }

        public async void SendBusy(string callerNumber)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Busy", callerNumber); } catch { }
        }

        public async void SendHold(string targetNumber)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Hold", targetNumber); } catch { }
        }

        public async void SendResume(string targetNumber)
        {
            if (IsConnected) try { await _hubConnection.InvokeAsync("Resume", targetNumber); } catch { }
        }

        #endregion

        #region IDisposable 実装

        public async void Dispose()
        {
            _isManuallyDisconnecting = true; // 再接続ループを止める
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }
        }

        #endregion
    }
}