using System;
using System.Windows;

namespace RailwayPhone
{
    public class Program : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new Program();

            // 1. アプリが勝手に終了しないようにモードを設定
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 2. 最初に自局選択ウィンドウを出す
            var selectionWindow = new StationSelectionWindow();
            bool? result = selectionWindow.ShowDialog();

            // 3. OKが押されたらメイン画面へ
            if (result == true)
            {
                // 選ばれた駅情報をメイン画面に渡して起動
                var selectedStation = selectionWindow.SelectedStation;
                var mainWindow = new MainWindow(selectedStation);

                // メイン画面が閉じたらアプリも終わるように戻す
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.Run(mainWindow);
            }
            else
            {
                // キャンセルされたらアプリ終了
                app.Shutdown();
            }
        }
    }
}