using System;
using System.Windows;

namespace RailwayPhone
{
    // アプリケーションの起動ロジックを管理するクラス
    public class Program : Application
    {
        // アプリのエントリーポイント（ここから始まります）
        [STAThread]
        public static void Main()
        {
            // 1. アプリケーションのインスタンスを作成
            var app = new Program();

            // 「自局選択画面」を閉じたときにアプリまで終了しないように設定
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // 2. 自局選択ウィンドウを表示
                var selectionWindow = new StationSelectionWindow();
                bool? result = selectionWindow.ShowDialog();

                // 3. OKボタンが押された場合のみメイン画面へ進む
                if (result == true)
                {
                    // 選択された駅情報を取得
                    var selectedStation = selectionWindow.SelectedStation;

                    // ★安全対策: 万が一 null だった場合の保険
                    if (selectedStation == null)
                    {
                        MessageBox.Show("自局データが正しく取得できませんでした。\nデフォルト設定（リストの先頭）で起動します。",
                                        "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

                        // リストにデータがあれば先頭を使う、なければダミーデータ
                        if (PhoneBook.Entries != null && PhoneBook.Entries.Count > 0)
                        {
                            selectedStation = PhoneBook.Entries[0];
                        }
                        else
                        {
                            selectedStation = new PhoneBookEntry { Name = "緊急用予備端末", Number = "999" };
                        }
                    }

                    // 4. メイン画面を作成してデータを渡す
                    var mainWindow = new MainWindow(selectedStation);

                    // メイン画面が閉じたらアプリも終了するように設定を戻す
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;

                    // アプリを開始
                    app.Run(mainWindow);
                }
                else
                {
                    // キャンセルされた場合はアプリを終了
                    app.Shutdown();
                }
            }
            catch (Exception ex)
            {
                // 起動中にエラーが起きた場合にメッセージを出す
                MessageBox.Show($"起動時に致命的なエラーが発生しました:\n{ex.Message}\n\n{ex.StackTrace}",
                                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown();
            }
        }
    }
}