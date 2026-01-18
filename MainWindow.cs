using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Media.Animation; // アニメーション用
using CommunityToolkit.WinUI.Notifications;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace RailwayPhone
{
    public enum PhoneStatus
    {
        Idle,       // 待機
        Incoming,   // 着信
        Outgoing,   // 発信
        Talking,    // 通話中
        Holding     // 保留中
    }

    public class MainWindow : Window
    {
        // --- 通信設定 ---
        private CommunicationManager _commManager;
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_PORT = 8888;

        // --- デバイス設定 ---
        private DeviceInfo _currentInputDevice;
        private DeviceInfo _currentOutputDevice;
        private float _currentInputVol = 1.0f;
        private float _currentOutputVol = 1.0f;

        // --- データ ---
        private PhoneBookEntry _currentStation;
        private string _connectedTargetNumber;

        public PhoneStatus CurrentStatus { get; private set; } = PhoneStatus.Idle;
        private SoundManager _soundManager;

        // --- UIコントロール ---
        private ListView _phoneBookList;
        private TextBlock _selfStationDisplay;

        // パネル
        private Grid _rightPanelContainer;
        private UIElement _viewKeypad;
        private UIElement _viewIncoming;
        private UIElement _viewOutgoing;
        private UIElement _viewTalking;

        // テキスト部品
        private TextBlock _statusNameText;
        private TextBox _inputNumberBox;
        private TextBlock _incomingNameText;
        private TextBlock _incomingNumberText;
        private TextBlock _outgoingNameText;
        private TextBlock _outgoingNumberText;

        private TextBlock _talkingStatusText;
        private TextBlock _talkingNameText;
        private TextBlock _talkingTimerText;
        private Button _holdBtn;

        // アニメーション用パーツ
        private ScaleTransform _incomingPulse;
        private ScaleTransform _outgoingPulse;
        private ScaleTransform _talkingPulse;
        private Ellipse _talkingIconBg;

        // ロジック変数
        private DispatcherTimer _callTimer;
        private DateTime _callStartTime;
        private bool _isHolding = false;
        private Random _random = new Random();

        // カラー定義
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
        private readonly Brush _acceptColor = new SolidColorBrush(Color.FromRgb(30, 180, 50));
        private readonly Brush _holdColor = new SolidColorBrush(Color.FromRgb(255, 140, 0));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        public MainWindow(PhoneBookEntry station)
        {
            if (station == null) station = new PhoneBookEntry { Name = "設定なし", Number = "000" };
            _currentStation = station;

            Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]";
            Width = 950; Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bgColor;

            _soundManager = new SoundManager();

            InitializeComponents();

            _commManager = new CommunicationManager();
            _commManager.MessageReceived += OnMessageReceived;
            ConnectToServer();

            Closing += (s, e) => {
                if (_soundManager != null) _soundManager.Dispose();
                if (_commManager != null) _commManager.Dispose();
                ToastNotificationManagerCompat.Uninstall();
            };
        }

        // ★★★ 修正箇所: Dispatcher.Invokeを追加 ★★★
        private async void ConnectToServer()
        {
            Title += " [接続試行中...]"; // ここはOK

            bool success = await _commManager.Connect(SERVER_IP, SERVER_PORT);

            // awaitの後は別スレッドの可能性があるため、Dispatcher.InvokeでUIスレッドに戻す
            Dispatcher.Invoke(() =>
            {
                Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]";

                if (success)
                {
                    _commManager.SendLogin(_currentStation.Number);
                    Title += " [オンライン]";
                }
                else
                {
                    Title += " [オフライン - サーバー未検出]";
                }
            });
        }

        private void OnMessageReceived(string json)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (data == null || !data.ContainsKey("type")) return;

                    string type = data["type"];
                    string fromNumber = data.ContainsKey("from") ? data["from"] : "";

                    switch (type)
                    {
                        case "INCOMING":
                            HandleRealIncomingCall(fromNumber);
                            break;
                        case "ANSWERED":
                            HandleRealAnswered();
                            break;
                        case "HANGUP":
                            EndCall(playStatusSound: true);
                            break;
                    }
                }
                catch { }
            });
        }

        private void ChangeStatus(PhoneStatus newStatus)
        {
            CurrentStatus = newStatus;
        }

        private async void StartOutgoingCall()
        {
            string targetNum = _inputNumberBox.Text;
            if (string.IsNullOrEmpty(targetNum)) return;

            _connectedTargetNumber = targetNum;
            ChangeStatus(PhoneStatus.Outgoing);

            _viewKeypad.Visibility = Visibility.Collapsed;
            _viewTalking.Visibility = Visibility.Collapsed;
            _viewIncoming.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Visible;

            _outgoingNameText.Text = _statusNameText.Text;
            _outgoingNumberText.Text = targetNum;

            StartAnimation(_outgoingPulse, 0.8);

            if (_currentOutputDevice != null) _soundManager.SetOutputDevice(_currentOutputDevice.ID);
            _soundManager.Play(SoundManager.FILE_TORI);
            await Task.Delay(800);

            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                _commManager.SendCall(targetNum);
                _soundManager.Play(SoundManager.FILE_YOBIDASHI, loop: true);
            }
        }

        private void HandleRealIncomingCall(string fromNumber)
        {
            if (CurrentStatus != PhoneStatus.Idle) return;

            _connectedTargetNumber = fromNumber;
            var caller = PhoneBook.Entries.FirstOrDefault(x => x.Number == fromNumber);
            string callerName = caller != null ? caller.Name : "不明な発信者";

            ChangeStatus(PhoneStatus.Incoming);

            _viewKeypad.Visibility = Visibility.Collapsed;
            _viewTalking.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed;
            _viewIncoming.Visibility = Visibility.Visible;

            _incomingNameText.Text = callerName;
            _incomingNumberText.Text = fromNumber;

            StartAnimation(_incomingPulse, 0.5);

            if (_currentOutputDevice != null) _soundManager.SetOutputDevice(_currentOutputDevice.ID);

            if (fromNumber.StartsWith("1")) _soundManager.Play(SoundManager.FILE_YOBI2, loop: true);
            else _soundManager.Play(SoundManager.FILE_YOBI1, loop: true);

            new ToastContentBuilder().AddText("着信あり").AddText($"{callerName} ({fromNumber})").Show();
        }

        private void AnswerCall()
        {
            StopAnimation(_incomingPulse);
            _soundManager.Stop();
            _soundManager.Play(SoundManager.FILE_TORI);

            _commManager.SendAnswer(_connectedTargetNumber);
            GoToTalkingScreen(isOutgoing: false);
        }

        private void HandleRealAnswered()
        {
            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                _soundManager.Stop();
                GoToTalkingScreen(isOutgoing: true);
            }
        }

        private void GoToTalkingScreen(bool isOutgoing)
        {
            ChangeStatus(PhoneStatus.Talking);

            StopAnimation(_outgoingPulse);
            _viewIncoming.Visibility = Visibility.Collapsed;
            _viewKeypad.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed;
            _viewTalking.Visibility = Visibility.Visible;
            _isHolding = false;

            _talkingStatusText.Text = "通話中";
            _talkingStatusText.Foreground = _acceptColor;
            if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;
            UpdateHoldButton();

            StartAnimation(_talkingPulse, 1.2);

            if (isOutgoing) _talkingNameText.Text = _outgoingNameText.Text;
            else _talkingNameText.Text = _incomingNameText.Text;

            _callStartTime = DateTime.Now;
            _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _callTimer.Tick += (s, e) => { var span = DateTime.Now - _callStartTime; _talkingTimerText.Text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}"; };
            _callTimer.Start();
        }

        private void ToggleHold()
        {
            _isHolding = !_isHolding;
            UpdateHoldStatusUI();
        }

        private void UpdateHoldStatusUI()
        {
            if (_isHolding)
            {
                ChangeStatus(PhoneStatus.Holding);
                int rnd = _random.Next(0, 2);
                string holdFile = (rnd == 0) ? SoundManager.FILE_HOLD1 : SoundManager.FILE_HOLD2;
                _soundManager.Play(holdFile, loop: true);

                _holdBtn.Content = "再 開"; _holdBtn.Background = _holdColor;
                _talkingStatusText.Text = "保留中"; _talkingStatusText.Foreground = _holdColor;
                if (_talkingIconBg != null) _talkingIconBg.Fill = _holdColor;
                StopAnimation(_talkingPulse);
            }
            else
            {
                ChangeStatus(PhoneStatus.Talking);
                _soundManager.Stop();

                _holdBtn.Content = "保 留"; _holdBtn.Background = Brushes.Gray;
                _talkingStatusText.Text = "通話中"; _talkingStatusText.Foreground = _acceptColor;
                if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;
                StartAnimation(_talkingPulse, 1.2);
            }
        }

        private void UpdateHoldButton()
        {
            _holdBtn.Content = "保 留";
            _holdBtn.Background = Brushes.Gray;
        }

        private void EndCall(bool playStatusSound = true)
        {
            if (!string.IsNullOrEmpty(_connectedTargetNumber))
            {
                _commManager.SendHangup(_connectedTargetNumber);
                _connectedTargetNumber = null;
            }

            StopAnimation(_talkingPulse);
            StopAnimation(_outgoingPulse);
            StopAnimation(_incomingPulse);
            _callTimer?.Stop();

            if (playStatusSound) _soundManager.Play(SoundManager.FILE_OKI);
            else _soundManager.Stop();

            ChangeStatus(PhoneStatus.Idle);

            _viewIncoming.Visibility = Visibility.Collapsed;
            _viewTalking.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed;
            _viewKeypad.Visibility = Visibility.Visible;
            _inputNumberBox.Text = "";
            _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray;
        }

        private void OpenStationSettings(object sender, RoutedEventArgs e)
        {
            var win = new StationSelectionWindow(_currentStation);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _currentStation = win.SelectedStation;
                Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]";
                _selfStationDisplay.Text = $"自局: {_currentStation.Name} ({_currentStation.Number})";
                _commManager.SendLogin(_currentStation.Number);
                Title += " [オンライン(更新)]";
            }
        }

        private void InitializeComponents()
        {
            var dockPanel = new DockPanel();
            var menu = new Menu { Background = Brushes.White, Padding = new Thickness(5) };
            menu.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 };
            DockPanel.SetDock(menu, Dock.Top);
            var settingsItem = new MenuItem { Header = "設定(_S)" };
            var audioItem = new MenuItem { Header = "音声設定(_A)..." }; audioItem.Click += OpenAudioSettings; settingsItem.Items.Add(audioItem);
            var stationItem = new MenuItem { Header = "自局設定(_M)..." }; stationItem.Click += OpenStationSettings; settingsItem.Items.Add(stationItem);
            settingsItem.Items.Add(new Separator());
            var exitItem = new MenuItem { Header = "終了(_X)" }; exitItem.Click += (s, e) => Close(); settingsItem.Items.Add(exitItem);
            menu.Items.Add(settingsItem);

            var testItem = new MenuItem { Header = "テスト(_T)" };
            var simItem = new MenuItem { Header = "自己着信テスト" };
            simItem.Click += (s, e) => HandleRealIncomingCall("999");
            testItem.Items.Add(simItem);
            menu.Items.Add(testItem);
            dockPanel.Children.Add(menu);

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = new DockPanel { Margin = new Thickness(15, 15, 5, 15) };
            var listHeader = new TextBlock { Text = "連絡先リスト", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(5, 0, 0, 10) };
            DockPanel.SetDock(listHeader, Dock.Top);
            _phoneBookList = new ListView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(_phoneBookList, ScrollBarVisibility.Disabled);
            var itemTemplate = new DataTemplate();
            var fb = new FrameworkElementFactory(typeof(Border)); fb.SetValue(Border.BackgroundProperty, Brushes.White); fb.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); fb.SetValue(Border.PaddingProperty, new Thickness(10)); fb.SetValue(Border.MarginProperty, new Thickness(2, 0, 5, 8)); fb.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 });
            var fg = new FrameworkElementFactory(typeof(Grid));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition)); c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(40));
            var c2 = new FrameworkElementFactory(typeof(ColumnDefinition)); c2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c3 = new FrameworkElementFactory(typeof(ColumnDefinition)); c3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            fg.AppendChild(c1); fg.AppendChild(c2); fg.AppendChild(c3);
            var fe = new FrameworkElementFactory(typeof(Ellipse)); fe.SetValue(Ellipse.WidthProperty, 32.0); fe.SetValue(Ellipse.HeightProperty, 32.0); fe.SetValue(Ellipse.FillProperty, Brushes.WhiteSmoke); fe.SetValue(Grid.ColumnProperty, 0); fe.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); fg.AppendChild(fe);
            var fit = new FrameworkElementFactory(typeof(TextBlock)); fit.SetValue(TextBlock.TextProperty, "📞"); fit.SetValue(TextBlock.FontSizeProperty, 14.0); fit.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); fit.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); fit.SetValue(Grid.ColumnProperty, 0); fg.AppendChild(fit);
            var fn = new FrameworkElementFactory(typeof(TextBlock)); fn.SetBinding(TextBlock.TextProperty, new Binding("Name")); fn.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); fn.SetValue(TextBlock.FontSizeProperty, 13.0); fn.SetValue(TextBlock.ForegroundProperty, Brushes.Black); fn.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); fn.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0)); fn.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); fn.SetValue(Grid.ColumnProperty, 1); fg.AppendChild(fn);
            var fnum = new FrameworkElementFactory(typeof(TextBlock)); fnum.SetBinding(TextBlock.TextProperty, new Binding("Number")); fnum.SetValue(TextBlock.FontSizeProperty, 16.0); fnum.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas")); fnum.SetValue(TextBlock.ForegroundProperty, _primaryColor); fnum.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); fnum.SetValue(Grid.ColumnProperty, 2); fg.AppendChild(fnum);
            fb.AppendChild(fg); itemTemplate.VisualTree = fb;
            _phoneBookList.ItemTemplate = itemTemplate;
            _phoneBookList.ItemsSource = PhoneBook.Entries;
            _phoneBookList.SelectionChanged += (s, e) => { var sel = _phoneBookList.SelectedItem as PhoneBookEntry; if (sel != null) _inputNumberBox.Text = sel.Number; };
            leftPanel.Children.Add(_phoneBookList);
            Grid.SetColumn(leftPanel, 0); mainGrid.Children.Add(leftPanel);

            var rightWrapper = new Grid { Margin = new Thickness(5, 15, 15, 15) };
            var rightCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 10 } };
            rightWrapper.Children.Add(rightCard);
            _rightPanelContainer = new Grid();
            rightCard.Child = _rightPanelContainer;

            _viewKeypad = CreateKeypadView(); _rightPanelContainer.Children.Add(_viewKeypad);
            _viewIncoming = CreateIncomingView(); _viewIncoming.Visibility = Visibility.Collapsed; _rightPanelContainer.Children.Add(_viewIncoming);
            _viewOutgoing = CreateOutgoingView(); _viewOutgoing.Visibility = Visibility.Collapsed; _rightPanelContainer.Children.Add(_viewOutgoing);
            _viewTalking = CreateTalkingView(); _viewTalking.Visibility = Visibility.Collapsed; _rightPanelContainer.Children.Add(_viewTalking);

            Grid.SetColumn(rightWrapper, 1); mainGrid.Children.Add(rightWrapper);
            dockPanel.Children.Add(mainGrid); Content = dockPanel;
        }

        private Grid CreatePulsingIcon(Brush color, out ScaleTransform transform) { var g = new Grid { Width = 100, Height = 100, Margin = new Thickness(0, 0, 0, 20) }; var el = new Ellipse { Fill = color, Opacity = 0.2 }; var tx = new TextBlock { Text = "📞", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = color }; transform = new ScaleTransform(1.0, 1.0, 50, 50); el.RenderTransform = transform; g.Children.Add(el); g.Children.Add(tx); return g; }
        private UIElement CreateKeypadView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 280 }; _selfStationDisplay = new TextBlock { Text = $"自局: {_currentStation.Name} ({_currentStation.Number})", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30), Background = new SolidColorBrush(Color.FromRgb(230, 240, 255)), Padding = new Thickness(10, 5, 10, 5) }; p.Children.Add(_selfStationDisplay); _statusNameText = new TextBlock { Text = "宛先未指定", FontSize = 20, FontWeight = FontWeights.Light, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) }; p.Children.Add(_statusNameText); _inputNumberBox = new TextBox { Text = "", FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalContentAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = _primaryColor, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 30), FontFamily = new FontFamily("Consolas") }; _inputNumberBox.TextChanged += OnInputNumberChanged; p.Children.Add(_inputNumberBox); var kg = new Grid { Margin = new Thickness(0, 0, 0, 30), HorizontalAlignment = HorizontalAlignment.Center }; for (int i = 0; i < 3; i++) kg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); for (int i = 0; i < 4; i++) kg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) }); int num = 1; for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) { var btn = CreateDialButton(num.ToString()); Grid.SetRow(btn, row); Grid.SetColumn(btn, col); kg.Children.Add(btn); num++; } } var bs = CreateDialButton("*"); Grid.SetRow(bs, 3); Grid.SetColumn(bs, 0); kg.Children.Add(bs); var bz = CreateDialButton("0"); Grid.SetRow(bz, 3); Grid.SetColumn(bz, 1); kg.Children.Add(bz); var bd = new Button { Content = "⌫", FontSize = 20, FontWeight = FontWeights.Bold, Background = Brushes.WhiteSmoke, Foreground = Brushes.DimGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var ds = new Style(typeof(Border)); ds.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); bd.Resources.Add(typeof(Border), ds); bd.Click += (s, e) => { var t = _inputNumberBox.Text; if (!string.IsNullOrEmpty(t)) { _inputNumberBox.Text = t.Substring(0, t.Length - 1); _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); } }; Grid.SetRow(bd, 3); Grid.SetColumn(bd, 2); kg.Children.Add(bd); p.Children.Add(kg); var cb = new Button { Content = "発 信", Height = 50, FontSize = 18, FontWeight = FontWeights.Bold, Background = _primaryColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(10, 0, 10, 0) }; var cs = new Style(typeof(Border)); cs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(25))); cb.Resources.Add(typeof(Border), cs); cb.Click += (s, e) => StartOutgoingCall(); p.Children.Add(cb); return p; }
        private UIElement CreateIncomingView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; p.Children.Add(new TextBlock { Text = "着信中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }); p.Children.Add(CreatePulsingIcon(_primaryColor, out _incomingPulse)); _incomingNameText = new TextBlock { Text = "---", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(_incomingNameText); _incomingNumberText = new TextBlock { Text = "---", FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) }; p.Children.Add(_incomingNumberText); var bg = new Grid { Width = 300 }; bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); var ab = new Button { Content = "📞 応答", Height = 60, Background = _acceptColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var @as = new Style(typeof(Border)); @as.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); ab.Resources.Add(typeof(Border), @as); ab.Click += (s, e) => AnswerCall(); Grid.SetColumn(ab, 0); bg.Children.Add(ab); var rb = new Button { Content = "切断", Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var rs = new Style(typeof(Border)); rs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); rb.Resources.Add(typeof(Border), rs); rb.Click += (s, e) => EndCall(false); Grid.SetColumn(rb, 2); bg.Children.Add(rb); p.Children.Add(bg); return p; }
        private UIElement CreateOutgoingView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; p.Children.Add(new TextBlock { Text = "呼び出し中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }); p.Children.Add(CreatePulsingIcon(_primaryColor, out _outgoingPulse)); _outgoingNameText = new TextBlock { Text = "---", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(_outgoingNameText); _outgoingNumberText = new TextBlock { Text = "---", FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) }; p.Children.Add(_outgoingNumberText); var cb = new Button { Content = "取 消", Width = 200, Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var cs = new Style(typeof(Border)); cs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); cb.Resources.Add(typeof(Border), cs); cb.Click += (s, e) => EndCall(true); p.Children.Add(cb); return p; }
        private UIElement CreateTalkingView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; _talkingStatusText = new TextBlock { Text = "通話中", FontSize = 16, Foreground = _acceptColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.Bold }; p.Children.Add(_talkingStatusText); var ig = CreatePulsingIcon(_acceptColor, out _talkingPulse); _talkingIconBg = ig.Children.OfType<Ellipse>().FirstOrDefault(); p.Children.Add(ig); _talkingNameText = new TextBlock { Text = "---", FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 20) }; p.Children.Add(_talkingNameText); _talkingTimerText = new TextBlock { Text = "00:00", FontSize = 48, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.DarkSlateGray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) }; p.Children.Add(_talkingTimerText); var bg = new Grid { Width = 300 }; bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); _holdBtn = new Button { Content = "保 留", Height = 60, Background = Brushes.Gray, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var hs = new Style(typeof(Border)); hs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); _holdBtn.Resources.Add(typeof(Border), hs); _holdBtn.Click += (s, e) => ToggleHold(); Grid.SetColumn(_holdBtn, 0); bg.Children.Add(_holdBtn); var eb = new Button { Content = "終 話", Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var es = new Style(typeof(Border)); es.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); eb.Resources.Add(typeof(Border), es); eb.Click += (s, e) => EndCall(true); Grid.SetColumn(eb, 2); bg.Children.Add(eb); p.Children.Add(bg); return p; }

        private void StartAnimation(ScaleTransform target, double durationSec) { if (target == null) return; var ax = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }; var ay = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }; target.BeginAnimation(ScaleTransform.ScaleXProperty, ax); target.BeginAnimation(ScaleTransform.ScaleYProperty, ay); }
        private void StopAnimation(ScaleTransform target) { if (target == null) return; target.BeginAnimation(ScaleTransform.ScaleXProperty, null); target.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        private Button CreateDialButton(string t) { var b = new Button { Content = t, FontSize = 24, FontWeight = FontWeights.SemiBold, Background = Brushes.White, Foreground = Brushes.DarkSlateGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var s = new Style(typeof(Border)); s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); b.Resources.Add(typeof(Border), s); b.Click += (o, e) => { _inputNumberBox.Text += t; _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); }; return b; }
        private void OnInputNumberChanged(object s, TextChangedEventArgs e) { string cur = _inputNumberBox.Text; if (string.IsNullOrWhiteSpace(cur)) { _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray; return; } var m = PhoneBook.Entries.FirstOrDefault(x => x.Number == cur); if (m != null) { _statusNameText.Text = m.Name; _statusNameText.Foreground = Brushes.Black; _phoneBookList.SelectedItem = m; _phoneBookList.ScrollIntoView(m); } else { _statusNameText.Text = "未登録の番号"; _statusNameText.Foreground = Brushes.Gray; _phoneBookList.SelectedItem = null; } }
        private void OpenAudioSettings(object sender, RoutedEventArgs e) { var win = new AudioSettingWindow(_currentInputDevice, _currentOutputDevice, _currentInputVol, _currentOutputVol); win.Owner = this; if (win.ShowDialog() == true) { _currentInputDevice = win.SelectedInput; _currentOutputDevice = win.SelectedOutput; _currentInputVol = win.InputVolume; _currentOutputVol = win.OutputVolume; } }
    }
}