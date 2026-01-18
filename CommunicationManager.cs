using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // MessageBox用

namespace RailwayPhone
{
    public class CommunicationManager : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected = false;

        // サーバーからメッセージが届いたときに発生するイベント
        public event Action<string> MessageReceived;

        // 接続処理
        public async Task<bool> Connect(string ipAddress, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ipAddress, port);
                _stream = _client.GetStream();
                _isConnected = true;

                // 受信タスクを開始（裏でずっと聞き耳を立てる）
                _ = Task.Run(ReceiveLoop);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ログイン情報を送る
        public void SendLogin(string number)
        {
            var data = new { type = "LOGIN", number = number };
            SendMessage(data);
        }

        // 汎用的な送信メソッド
        public void SendMessage(object data)
        {
            if (!_isConnected) return;
            try
            {
                string json = JsonSerializer.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                _stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"送信エラー: {ex.Message}");
                _isConnected = false;
            }
        }

        // 受信ループ（裏で動き続ける）
        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (_isConnected)
                {
                    // データが来るまでここで待機
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // 切断された

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // イベントを発火してメイン画面に知らせる
                    MessageReceived?.Invoke(json);
                }
            }
            catch
            {
                // エラーまたは切断
            }
            finally
            {
                _isConnected = false;
            }
        }

        public void Dispose()
        {
            _isConnected = false;
            _stream?.Close();
            _client?.Close();
        }
    }
}