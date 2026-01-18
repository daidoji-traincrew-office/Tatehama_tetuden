using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Effects;

namespace RailwayPhone
{
    public class MainWindow : Window
    {
        private DeviceInfo _currentInputDevice;
        private DeviceInfo _currentOutputDevice;
        private float _currentInputVol = 1.0f;
        private float _currentOutputVol = 1.0f;

        // 自局情報 (起動時に決定済みなので null にはならない前提)
        private PhoneBookEntry _currentStation;

        // UI
        private TextBlock _statusNameText;
        private TextBox _inputNumberBox;
        private ListView _phoneBookList;
        private TextBlock _selfStationDisplay;

        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        // ★コンストラクタ引数で必ず station を受け取る
        public MainWindow(PhoneBookEntry station)
        {
            _currentStation = station;

            Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]";
            Width = 950; Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bgColor;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var dockPanel = new DockPanel();

            // --- 1. メニューバー (項目を分割) ---
            var menu = new Menu { Background = Brushes.White, Padding = new Thickness(5) };
            menu.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 };
            DockPanel.SetDock(menu, Dock.Top);

            var settingsItem = new MenuItem { Header = "設定(_S)" };

            // ★項目1: 音声設定
            var audioItem = new MenuItem { Header = "音声設定(_A)..." };
            audioItem.Click += OpenAudioSettings;
            settingsItem.Items.Add(audioItem);

            // ★項目2: 自局設定
            var stationItem = new MenuItem { Header = "自局設定(_M)..." };
            stationItem.Click += OpenStationSettings;
            settingsItem.Items.Add(stationItem);

            settingsItem.Items.Add(new Separator());

            var exitItem = new MenuItem { Header = "終了(_X)" };
            exitItem.Click += (s, e) => Close();
            settingsItem.Items.Add(exitItem);

            menu.Items.Add(settingsItem);
            dockPanel.Children.Add(menu);

            // --- 2. メインレイアウト ---
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左側: 電話帳
            var leftPanel = new DockPanel { Margin = new Thickness(15, 15, 5, 15) };
            var listHeader = new TextBlock { Text = "連絡先リスト", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(5, 0, 0, 10) };
            DockPanel.SetDock(listHeader, Dock.Top);

            _phoneBookList = new ListView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(_phoneBookList, ScrollBarVisibility.Disabled);

            var itemTemplate = new DataTemplate();
            var factoryBorder = new FrameworkElementFactory(typeof(Border));
            factoryBorder.SetValue(Border.BackgroundProperty, Brushes.White); factoryBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); factoryBorder.SetValue(Border.PaddingProperty, new Thickness(10)); factoryBorder.SetValue(Border.MarginProperty, new Thickness(2, 0, 5, 8)); factoryBorder.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 });
            var factoryGrid = new FrameworkElementFactory(typeof(Grid));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition)); col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(40));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition)); col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col3 = new FrameworkElementFactory(typeof(ColumnDefinition)); col3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            factoryGrid.AppendChild(col1); factoryGrid.AppendChild(col2); factoryGrid.AppendChild(col3);

            var factoryEllipse = new FrameworkElementFactory(typeof(Ellipse)); factoryEllipse.SetValue(Ellipse.WidthProperty, 32.0); factoryEllipse.SetValue(Ellipse.HeightProperty, 32.0); factoryEllipse.SetValue(Ellipse.FillProperty, Brushes.WhiteSmoke); factoryEllipse.SetValue(Grid.ColumnProperty, 0); factoryEllipse.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); factoryGrid.AppendChild(factoryEllipse);
            var factoryIcon = new FrameworkElementFactory(typeof(TextBlock)); factoryIcon.SetValue(TextBlock.TextProperty, "📞"); factoryIcon.SetValue(TextBlock.FontSizeProperty, 14.0); factoryIcon.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); factoryIcon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); factoryIcon.SetValue(Grid.ColumnProperty, 0); factoryGrid.AppendChild(factoryIcon);
            var factoryName = new FrameworkElementFactory(typeof(TextBlock)); factoryName.SetBinding(TextBlock.TextProperty, new Binding("Name")); factoryName.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); factoryName.SetValue(TextBlock.FontSizeProperty, 13.0); factoryName.SetValue(TextBlock.ForegroundProperty, Brushes.Black); factoryName.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); factoryName.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0)); factoryName.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); factoryName.SetValue(Grid.ColumnProperty, 1); factoryGrid.AppendChild(factoryName);
            var factoryNum = new FrameworkElementFactory(typeof(TextBlock)); factoryNum.SetBinding(TextBlock.TextProperty, new Binding("Number")); factoryNum.SetValue(TextBlock.FontSizeProperty, 16.0); factoryNum.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas")); factoryNum.SetValue(TextBlock.ForegroundProperty, _primaryColor); factoryNum.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); factoryNum.SetValue(Grid.ColumnProperty, 2); factoryGrid.AppendChild(factoryNum);

            factoryBorder.AppendChild(factoryGrid); itemTemplate.VisualTree = factoryBorder;
            _phoneBookList.ItemTemplate = itemTemplate;
            _phoneBookList.ItemsSource = PhoneBook.Entries;
            _phoneBookList.SelectionChanged += (s, e) => { var sel = _phoneBookList.SelectedItem as PhoneBookEntry; if (sel != null) _inputNumberBox.Text = sel.Number; };

            leftPanel.Children.Add(_phoneBookList); Grid.SetColumn(leftPanel, 0); mainGrid.Children.Add(leftPanel);

            // 右側: 操作パネル
            var rightWrapper = new Grid { Margin = new Thickness(5, 15, 15, 15) };
            var rightCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 10 } };
            rightWrapper.Children.Add(rightCard);

            var rightContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 280 };

            // ★自局表示 (起動時から確定しているのでそのまま表示)
            _selfStationDisplay = new TextBlock
            {
                Text = $"自局: {_currentStation.Name} ({_currentStation.Number})",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = _primaryColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 30),
                Background = new SolidColorBrush(Color.FromRgb(230, 240, 255)),
                Padding = new Thickness(10, 5, 10, 5)
            };
            rightContent.Children.Add(_selfStationDisplay);

            _statusNameText = new TextBlock { Text = "宛先未指定", FontSize = 20, FontWeight = FontWeights.Light, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
            rightContent.Children.Add(_statusNameText);

            _inputNumberBox = new TextBox { Text = "", FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalContentAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = _primaryColor, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 30), FontFamily = new FontFamily("Consolas") };
            _inputNumberBox.TextChanged += OnInputNumberChanged;
            rightContent.Children.Add(_inputNumberBox);

            var keyPadGrid = new Grid { Margin = new Thickness(0, 0, 0, 30), HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 3; i++) keyPadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            for (int i = 0; i < 4; i++) keyPadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            int num = 1;
            for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) { var btn = CreateDialButton(num.ToString()); Grid.SetRow(btn, row); Grid.SetColumn(btn, col); keyPadGrid.Children.Add(btn); num++; } }
            var btnStar = CreateDialButton("*"); Grid.SetRow(btnStar, 3); Grid.SetColumn(btnStar, 0); keyPadGrid.Children.Add(btnStar);
            var btnZero = CreateDialButton("0"); Grid.SetRow(btnZero, 3); Grid.SetColumn(btnZero, 1); keyPadGrid.Children.Add(btnZero);
            var btnDel = new Button { Content = "⌫", FontSize = 20, FontWeight = FontWeights.Bold, Background = Brushes.WhiteSmoke, Foreground = Brushes.DimGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand };
            var dStyle = new Style(typeof(Border)); dStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); btnDel.Resources.Add(typeof(Border), dStyle);
            btnDel.Click += (s, e) => { var t = _inputNumberBox.Text; if (!string.IsNullOrEmpty(t)) { _inputNumberBox.Text = t.Substring(0, t.Length - 1); _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); } };
            Grid.SetRow(btnDel, 3); Grid.SetColumn(btnDel, 2); keyPadGrid.Children.Add(btnDel);
            rightContent.Children.Add(keyPadGrid);

            var callBtn = new Button { Content = "発 信", Height = 50, FontSize = 18, FontWeight = FontWeights.Bold, Background = _primaryColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(10, 0, 10, 0) };
            var cStyle = new Style(typeof(Border)); cStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(25))); callBtn.Resources.Add(typeof(Border), cStyle);
            rightContent.Children.Add(callBtn);

            rightCard.Child = rightContent;
            Grid.SetColumn(rightWrapper, 1); mainGrid.Children.Add(rightWrapper);
            dockPanel.Children.Add(mainGrid); Content = dockPanel;
        }

        private Button CreateDialButton(string t) { var b = new Button { Content = t, FontSize = 24, FontWeight = FontWeights.SemiBold, Background = Brushes.White, Foreground = Brushes.DarkSlateGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var s = new Style(typeof(Border)); s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); b.Resources.Add(typeof(Border), s); b.Click += (o, e) => { _inputNumberBox.Text += t; _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); }; return b; }
        private void OnInputNumberChanged(object s, TextChangedEventArgs e) { string cur = _inputNumberBox.Text; if (string.IsNullOrWhiteSpace(cur)) { _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray; return; } var m = PhoneBook.Entries.FirstOrDefault(x => x.Number == cur); if (m != null) { _statusNameText.Text = m.Name; _statusNameText.Foreground = Brushes.Black; _phoneBookList.SelectedItem = m; _phoneBookList.ScrollIntoView(m); } else { _statusNameText.Text = "未登録の番号"; _statusNameText.Foreground = Brushes.Gray; _phoneBookList.SelectedItem = null; } }

        // --- メニュー動作 ---

        // 音声設定を開く
        private void OpenAudioSettings(object sender, RoutedEventArgs e)
        {
            var win = new AudioSettingWindow(_currentInputDevice, _currentOutputDevice, _currentInputVol, _currentOutputVol);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _currentInputDevice = win.SelectedInput;
                _currentOutputDevice = win.SelectedOutput;
                _currentInputVol = win.InputVolume;
                _currentOutputVol = win.OutputVolume;
            }
        }

        // 自局設定を開く（StationSelectionWindowを再利用）
        private void OpenStationSettings(object sender, RoutedEventArgs e)
        {
            var win = new StationSelectionWindow(_currentStation);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _currentStation = win.SelectedStation;
                // 表示更新
                Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]";
                _selfStationDisplay.Text = $"自局: {_currentStation.Name} ({_currentStation.Number})";
            }
        }
    }
}