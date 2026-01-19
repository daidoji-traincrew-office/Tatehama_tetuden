using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic; // 辞書用

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

        // ログイン
        public void SendLogin(string myNumber)
        {
            SendMessage(new { type = "LOGIN", number = myNumber });
        }

        // 発信 (自分の音声ポート番号 udpPort を一緒に送る)
        public void SendCall(string targetNumber, int udpPort)
        {
            SendMessage(new
            {
                type = "CALL",
                target = targetNumber,
                udp_port = udpPort.ToString() // サーバー経由で相手に届く
            });
        }

        // 応答 (自分の音声ポート番号 udpPort を一緒に送る)
        public void SendAnswer(string targetNumber, int udpPort)
        {
            SendMessage(new
            {
                type = "ANSWER",
                target = targetNumber,
                udp_port = udpPort.ToString()
            });
        }

        // 切断
        public void SendHangup(string targetNumber)
        {
            SendMessage(new { type = "HANGUP", target = targetNumber });
        }

        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (_isConnected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    // JSONが複数くっついている場合の簡易対応などは今回は省略
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