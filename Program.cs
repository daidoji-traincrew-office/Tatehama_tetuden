using System;
using System.Windows;

namespace RailwayPhone
{
    // アプリケーションの起動ロジックのみを管理するクラス
    public class Program : Application
    {
        // アプリのエントリーポイント
        [STAThread]
        public static void Main()
        {
            var app = new Program();

            // 自局選択画面を閉じてもアプリが終了しないように設定
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // 1. 自局選択ウィンドウを表示
                var selectionWindow = new StationSelectionWindow();
                bool? result = selectionWindow.ShowDialog();

                // 2. OKが押された場合のみメイン画面へ進む
                if (result == true)
                {
                    var selectedStation = selectionWindow.SelectedStation;

                    // 安全対策: データが取れなかった場合のダミー
                    if (selectedStation == null)
                    {
                        selectedStation = new PhoneBookEntry { Name = "緊急用予備端末", Number = "999" };
                    }

                    // 3. メイン画面を作成
                    var mainWindow = new MainWindow(selectedStation);

                    // メイン画面が閉じたらアプリも終了するように戻す
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;

                    // アプリを開始
                    app.Run(mainWindow);
                }
                else
                {
                    // キャンセルされたら終了
                    app.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"起動エラー: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown();
            }
        }
    }
}