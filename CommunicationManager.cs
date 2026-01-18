using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RailwayPhone
{
    public class CommunicationManager : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected = false;

        public event Action<string> MessageReceived;

        public async Task<bool> Connect(string ipAddress, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ipAddress, port);
                _stream = _client.GetStream();
                _isConnected = true;
                _ = Task.Run(ReceiveLoop);
                return true;
            }
            catch { return false; }
        }

        // 基本送信
        public void SendMessage(object data)
        {
            if (!_isConnected) return;
            try
            {
                string json = JsonSerializer.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                _stream.Write(bytes, 0, bytes.Length);
            }
            catch { _isConnected = false; }
        }

        // --- ★ここから機能追加 ---

        // ログイン (場所変更時もこれを使う)
        public void SendLogin(string myNumber)
        {
            SendMessage(new { type = "LOGIN", number = myNumber });
        }

        // 発信 (相手の番号を指定)
        public void SendCall(string targetNumber)
        {
            SendMessage(new { type = "CALL", target = targetNumber });
        }

        // 応答 (相手に応答したことを伝える)
        public void SendAnswer(string targetNumber)
        {
            SendMessage(new { type = "ANSWER", target = targetNumber });
        }

        // 切断 (相手に切ったことを伝える)
        public void SendHangup(string targetNumber)
        {
            SendMessage(new { type = "HANGUP", target = targetNumber });
        }

        // --- ここまで ---

        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (_isConnected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    // 受信データが連結している可能性を考慮して分割処理等は省略（簡易実装）
                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    MessageReceived?.Invoke(json);
                }
            }
            catch { }
            finally { _isConnected = false; }
        }

        public void Dispose()
        {
            _isConnected = false;
            _stream?.Close();
            _client?.Close();
        }
    }
}