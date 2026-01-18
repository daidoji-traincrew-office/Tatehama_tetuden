using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RailwayPhone
{
    public class MainWindow : Window
    {
        private DeviceInfo _currentInputDevice;
        private DeviceInfo _currentOutputDevice;
        private TextBlock _statusText;

        public MainWindow()
        {
            Title = "模擬鉄 指令電話端末";
            Width = 600; Height = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var dockPanel = new DockPanel();

            // メニュー
            var menu = new Menu { Background = SystemColors.MenuBarBrush };
            DockPanel.SetDock(menu, Dock.Top);

            var settingsItem = new MenuItem { Header = "設定(_S)" };
            var propItem = new MenuItem { Header = "プロパティ(_P)..." };
            propItem.Click += OpenPropertySettings;
            settingsItem.Items.Add(propItem);

            var exitItem = new MenuItem { Header = "終了(_X)" };
            exitItem.Click += (s, e) => Close();
            settingsItem.Items.Add(new Separator());
            settingsItem.Items.Add(exitItem);

            menu.Items.Add(settingsItem);
            menu.Items.Add(new MenuItem { Header = "ヘルプ(_H)" });

            dockPanel.Children.Add(menu);

            // メインエリア
            var mainGrid = new Grid();
            _statusText = new TextBlock
            {
                Text = "準備完了 (デバイス未設定)",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
                Foreground = Brushes.Gray
            };
            mainGrid.Children.Add(_statusText);

            dockPanel.Children.Add(mainGrid);
            Content = dockPanel;
        }

        private void OpenPropertySettings(object sender, RoutedEventArgs e)
        {
            // 別ファイルに分けた PropertyWindow を呼び出す
            var propWindow = new PropertyWindow(_currentInputDevice, _currentOutputDevice);
            propWindow.Owner = this;

            if (propWindow.ShowDialog() == true)
            {
                _currentInputDevice = propWindow.SelectedInput;
                _currentOutputDevice = propWindow.SelectedOutput;
                UpdateStatusDisplay();
            }
        }

        private void UpdateStatusDisplay()
        {
            string inName = _currentInputDevice?.Name ?? "未設定";
            string outName = _currentOutputDevice?.Name ?? "未設定";

            _statusText.Text = $"設定適用済み:\nマイク: {inName}\nスピーカー: {outName}";
            _statusText.Foreground = Brushes.Black;
        }
    }
}