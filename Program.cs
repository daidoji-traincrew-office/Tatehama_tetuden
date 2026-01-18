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
            // MainWindowは別のファイルにあるが、
            // 同じ namespace RailwayPhone なので自動的に見える
            app.Run(new MainWindow());
        }
    }
}