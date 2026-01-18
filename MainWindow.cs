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

namespace RailwayPhone
{
    public enum PhoneStatus
    {
        Idle,       // 待機
        Incoming,   // 着信
        Outgoing,   // 発信(呼び出し中)
        Talking,    // 通話中
        Holding     // 保留中
    }

    public class MainWindow : Window
    {
        // データ
        private DeviceInfo _currentInputDevice;
        private DeviceInfo _currentOutputDevice;
        private float _currentInputVol = 1.0f;
        private float _currentOutputVol = 1.0f;
        private PhoneBookEntry _currentStation;

        public PhoneStatus CurrentStatus { get; private set; } = PhoneStatus.Idle;

        private SoundManager _soundManager;

        // UIコントロール
        private ListView _phoneBookList;
        private TextBlock _selfStationDisplay;

        // パネル
        private Grid _rightPanelContainer;
        private UIElement _viewKeypad;
        private UIElement _viewIncoming;
        private UIElement _viewOutgoing;
        private UIElement _viewTalking;

        // テキスト部品
        private TextBlock _statusNameText;     // Keypad
        private TextBox _inputNumberBox;       // Keypad
        private TextBlock _incomingNameText;   // Incoming
        private TextBlock _incomingNumberText; // Incoming
        private TextBlock _outgoingNameText;   // Outgoing
        private TextBlock _outgoingNumberText; // Outgoing

        private TextBlock _talkingStatusText;  // Talking (状態表示)
        private TextBlock _talkingNameText;    // Talking (相手名)
        private TextBlock _talkingTimerText;   // Talking (タイマー)
        private Button _holdBtn;

        // アニメーション用パーツ (各画面に配置)
        private ScaleTransform _incomingPulse; // 着信用
        private ScaleTransform _outgoingPulse; // 発信用
        private ScaleTransform _talkingPulse;  // 通話用
        private Ellipse _talkingIconBg;        // 通話アイコン背景(色変更用)

        // ロジック変数
        private DispatcherTimer _callTimer;
        private DateTime _callStartTime;
        private bool _isHolding = false;
        private Random _random = new Random();

        // カラー定義
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215)); // 青
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));  // 赤
        private readonly Brush _acceptColor = new SolidColorBrush(Color.FromRgb(30, 180, 50));  // 緑
        private readonly Brush _holdColor = new SolidColorBrush(Color.FromRgb(255, 140, 0));    // オレンジ
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        public MainWindow(PhoneBookEntry station)
        {
            if (station == null) station = new PhoneBookEntry { Name = "設定なし", Number = "000" };
            _currentStation = station;

            Title = $"館浜電鉄鉄道電話 - [{_currentStation.Name}]";
            Width = 950; Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bgColor;

            _soundManager = new SoundManager();

            InitializeComponents();
            ReportStatusToServer(PhoneStatus.Idle);

            Closing += (s, e) => {
                if (_soundManager != null) _soundManager.Dispose();
                ToastNotificationManagerCompat.Uninstall();
            };
        }

        private void ChangeStatus(PhoneStatus newStatus)
        {
            CurrentStatus = newStatus;
            ReportStatusToServer(newStatus);
        }

        private void ReportStatusToServer(PhoneStatus status)
        {
            string json = $@"{{ ""status"": ""{status}"", ""timestamp"": ""{DateTime.Now}"" }}";
            System.Diagnostics.Debug.WriteLine($"[API] {json}");
        }

        private void InitializeComponents()
        {
            var dockPanel = new DockPanel();

            // 1. メニュー
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
            var simItem = new MenuItem { Header = "着信シミュレーション実行" }; simItem.Click += (s, e) => SimulateIncomingCall(); testItem.Items.Add(simItem);
            menu.Items.Add(testItem);
            dockPanel.Children.Add(menu);

            // 2. レイアウト
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左: 電話帳
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

            // 右: コンテナ
            var rightWrapper = new Grid { Margin = new Thickness(5, 15, 15, 15) };
            var rightCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 10 } };
            rightWrapper.Children.Add(rightCard);

            _rightPanelContainer = new Grid();
            rightCard.Child = _rightPanelContainer;

            // 各ビュー生成
            _viewKeypad = CreateKeypadView();
            _rightPanelContainer.Children.Add(_viewKeypad);

            _viewIncoming = CreateIncomingView();
            _viewIncoming.Visibility = Visibility.Collapsed;
            _rightPanelContainer.Children.Add(_viewIncoming);

            _viewOutgoing = CreateOutgoingView();
            _viewOutgoing.Visibility = Visibility.Collapsed;
            _rightPanelContainer.Children.Add(_viewOutgoing);

            _viewTalking = CreateTalkingView();
            _viewTalking.Visibility = Visibility.Collapsed;
            _rightPanelContainer.Children.Add(_viewTalking);

            Grid.SetColumn(rightWrapper, 1); mainGrid.Children.Add(rightWrapper);
            dockPanel.Children.Add(mainGrid); Content = dockPanel;
        }

        // --- ビュー作成ヘルパー (アイコン生成) ---
        private Grid CreatePulsingIcon(Brush color, out ScaleTransform transform)
        {
            var grid = new Grid { Width = 100, Height = 100, Margin = new Thickness(0, 0, 0, 20) };
            var ellipse = new Ellipse { Fill = color, Opacity = 0.2 };
            var text = new TextBlock { Text = "📞", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = color };

            transform = new ScaleTransform(1.0, 1.0, 50, 50); // 中心点(50,50)
            ellipse.RenderTransform = transform;

            grid.Children.Add(ellipse);
            grid.Children.Add(text);
            return grid;
        }

        // 1. 待機画面
        private UIElement CreateKeypadView()
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 280 };
            _selfStationDisplay = new TextBlock { Text = $"自局: {_currentStation.Name} ({_currentStation.Number})", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30), Background = new SolidColorBrush(Color.FromRgb(230, 240, 255)), Padding = new Thickness(10, 5, 10, 5) };
            panel.Children.Add(_selfStationDisplay);
            _statusNameText = new TextBlock { Text = "宛先未指定", FontSize = 20, FontWeight = FontWeights.Light, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
            panel.Children.Add(_statusNameText);
            _inputNumberBox = new TextBox { Text = "", FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalContentAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = _primaryColor, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 30), FontFamily = new FontFamily("Consolas") };
            _inputNumberBox.TextChanged += OnInputNumberChanged;
            panel.Children.Add(_inputNumberBox);
            var keyPadGrid = new Grid { Margin = new Thickness(0, 0, 0, 30), HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 3; i++) keyPadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); for (int i = 0; i < 4; i++) keyPadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            int num = 1; for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) { var btn = CreateDialButton(num.ToString()); Grid.SetRow(btn, row); Grid.SetColumn(btn, col); keyPadGrid.Children.Add(btn); num++; } }
            var btnStar = CreateDialButton("*"); Grid.SetRow(btnStar, 3); Grid.SetColumn(btnStar, 0); keyPadGrid.Children.Add(btnStar);
            var btnZero = CreateDialButton("0"); Grid.SetRow(btnZero, 3); Grid.SetColumn(btnZero, 1); keyPadGrid.Children.Add(btnZero);
            var btnDel = new Button { Content = "⌫", FontSize = 20, FontWeight = FontWeights.Bold, Background = Brushes.WhiteSmoke, Foreground = Brushes.DimGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var dStyle = new Style(typeof(Border)); dStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); btnDel.Resources.Add(typeof(Border), dStyle);
            btnDel.Click += (s, e) => { var t = _inputNumberBox.Text; if (!string.IsNullOrEmpty(t)) { _inputNumberBox.Text = t.Substring(0, t.Length - 1); _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); } };
            Grid.SetRow(btnDel, 3); Grid.SetColumn(btnDel, 2); keyPadGrid.Children.Add(btnDel);
            panel.Children.Add(keyPadGrid);
            var callBtn = new Button { Content = "発 信", Height = 50, FontSize = 18, FontWeight = FontWeights.Bold, Background = _primaryColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(10, 0, 10, 0) }; var cStyle = new Style(typeof(Border)); cStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(25))); callBtn.Resources.Add(typeof(Border), cStyle);
            callBtn.Click += (s, e) => StartOutgoingCall();
            panel.Children.Add(callBtn);
            return panel;
        }

        // 2. 着信画面 (アイコン追加)
        private UIElement CreateIncomingView()
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

            panel.Children.Add(new TextBlock { Text = "着信中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });

            // ★パルスアイコン
            panel.Children.Add(CreatePulsingIcon(_primaryColor, out _incomingPulse));

            _incomingNameText = new TextBlock { Text = "---", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_incomingNameText);
            _incomingNumberText = new TextBlock { Text = "---", FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) };
            panel.Children.Add(_incomingNumberText);

            var btnGrid = new Grid { Width = 300 };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var answerBtn = new Button { Content = "📞 応答", Height = 60, Background = _acceptColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var aStyle = new Style(typeof(Border)); aStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); answerBtn.Resources.Add(typeof(Border), aStyle);
            answerBtn.Click += (s, e) => AnswerCall(); Grid.SetColumn(answerBtn, 0); btnGrid.Children.Add(answerBtn);
            var rejectBtn = new Button { Content = "切断", Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var rStyle = new Style(typeof(Border)); rStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); rejectBtn.Resources.Add(typeof(Border), rStyle);
            rejectBtn.Click += (s, e) => EndCall(); Grid.SetColumn(rejectBtn, 2); btnGrid.Children.Add(rejectBtn);
            panel.Children.Add(btnGrid);
            return panel;
        }

        // 3. 発信画面 (アイコン追加)
        private UIElement CreateOutgoingView()
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

            panel.Children.Add(new TextBlock { Text = "呼び出し中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });

            // ★パルスアイコン
            panel.Children.Add(CreatePulsingIcon(_primaryColor, out _outgoingPulse));

            _outgoingNameText = new TextBlock { Text = "---", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_outgoingNameText);
            _outgoingNumberText = new TextBlock { Text = "---", FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) };
            panel.Children.Add(_outgoingNumberText);

            var cancelBtn = new Button { Content = "取 消", Width = 200, Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var cStyle = new Style(typeof(Border)); cStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); cancelBtn.Resources.Add(typeof(Border), cStyle);
            cancelBtn.Click += (s, e) => EndCall();
            panel.Children.Add(cancelBtn);
            return panel;
        }

        // 4. 通話画面 (アイコン追加)
        private UIElement CreateTalkingView()
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

            _talkingStatusText = new TextBlock { Text = "通話中", FontSize = 16, Foreground = _acceptColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.Bold };
            panel.Children.Add(_talkingStatusText);

            // ★パルスアイコン (緑) - 背景色を変えるためにGridを取得
            var iconGrid = CreatePulsingIcon(_acceptColor, out _talkingPulse);
            _talkingIconBg = iconGrid.Children.OfType<Ellipse>().FirstOrDefault(); // 後で色を変えるため保持
            panel.Children.Add(iconGrid);

            _talkingNameText = new TextBlock { Text = "---", FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
            panel.Children.Add(_talkingNameText);
            _talkingTimerText = new TextBlock { Text = "00:00", FontSize = 48, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.DarkSlateGray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
            panel.Children.Add(_talkingTimerText);

            var btnGrid = new Grid { Width = 300 };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _holdBtn = new Button { Content = "保 留", Height = 60, Background = Brushes.Gray, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var hStyle = new Style(typeof(Border)); hStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); _holdBtn.Resources.Add(typeof(Border), hStyle);
            _holdBtn.Click += (s, e) => ToggleHold(); Grid.SetColumn(_holdBtn, 0); btnGrid.Children.Add(_holdBtn);
            var endBtn = new Button { Content = "終 話", Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var eStyle = new Style(typeof(Border)); eStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); endBtn.Resources.Add(typeof(Border), eStyle);
            endBtn.Click += (s, e) => EndCall(); Grid.SetColumn(endBtn, 2); btnGrid.Children.Add(endBtn);
            panel.Children.Add(btnGrid);
            return panel;
        }

        // --- アニメーション制御 ---
        private void StartAnimation(ScaleTransform target, double durationSec)
        {
            if (target == null) return;
            var animX = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var animY = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            target.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        }

        private void StopAnimation(ScaleTransform target)
        {
            if (target == null) return;
            target.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        // --- ロジック ---

        private void SimulateIncomingCall()
        {
            try
            {
                if (_currentStation == null || PhoneBook.Entries == null) return;
                var others = PhoneBook.Entries.Where(x => x.Number != _currentStation.Number).ToList();
                if (others.Count == 0) return;
                var caller = others[new Random().Next(others.Count)];

                ChangeStatus(PhoneStatus.Incoming);

                _viewKeypad.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Collapsed; _viewOutgoing.Visibility = Visibility.Collapsed;
                _viewIncoming.Visibility = Visibility.Visible;

                _incomingNameText.Text = caller.Name;
                if (_incomingNumberText != null) _incomingNumberText.Text = caller.Number;

                // アニメ開始 (速め: 0.5秒)
                StartAnimation(_incomingPulse, 0.5);

                if (_currentOutputDevice != null) _soundManager.SetOutputDevice(_currentOutputDevice.ID);
                if (caller.Number.StartsWith("1")) _soundManager.Play(SoundManager.FILE_YOBI2, loop: true);
                else _soundManager.Play(SoundManager.FILE_YOBI1, loop: true);

                new ToastContentBuilder().AddText("着信あり").AddText($"{caller.Name} ({caller.Number})").Show();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void StartOutgoingCall()
        {
            if (string.IsNullOrEmpty(_inputNumberBox.Text)) return;

            ChangeStatus(PhoneStatus.Outgoing);

            _viewKeypad.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Collapsed; _viewIncoming.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Visible;

            _outgoingNameText.Text = _statusNameText.Text;
            _outgoingNumberText.Text = _inputNumberBox.Text;

            // アニメ開始 (速め: 0.8秒)
            StartAnimation(_outgoingPulse, 0.8);

            if (_currentOutputDevice != null) _soundManager.SetOutputDevice(_currentOutputDevice.ID);

            _soundManager.Play(SoundManager.FILE_TORI);
            await Task.Delay(800);

            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                _soundManager.Play(SoundManager.FILE_YOBIDASHI, loop: true);
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (s, e) => { t.Stop(); AnswerCall(isOutgoing: true); };
                t.Start();
            }
        }

        private void AnswerCall(bool isOutgoing = false)
        {
            StopAnimation(_outgoingPulse);
            StopAnimation(_incomingPulse);
            _soundManager.Stop();
            _soundManager.Play(SoundManager.FILE_TORI);

            ChangeStatus(PhoneStatus.Talking);

            _viewIncoming.Visibility = Visibility.Collapsed; _viewKeypad.Visibility = Visibility.Collapsed; _viewOutgoing.Visibility = Visibility.Collapsed;
            _viewTalking.Visibility = Visibility.Visible;
            _isHolding = false;

            // アニメ開始 (ゆったり: 1.2秒)
            StartAnimation(_talkingPulse, 1.2);
            UpdateHoldStatusUI();

            if (isOutgoing) _talkingNameText.Text = _statusNameText.Text;
            else _talkingNameText.Text = _incomingNameText.Text;

            _callStartTime = DateTime.Now;
            _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _callTimer.Tick += (s, e) => { var span = DateTime.Now - _callStartTime; _talkingTimerText.Text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}"; };
            _callTimer.Start();
        }

        private void ToggleHold()
        {
            _isHolding = !_isHolding;

            if (_isHolding)
            {
                ChangeStatus(PhoneStatus.Holding);
                int rnd = _random.Next(0, 2);
                string holdFile = (rnd == 0) ? SoundManager.FILE_HOLD1 : SoundManager.FILE_HOLD2;
                _soundManager.Play(holdFile, loop: true);
            }
            else
            {
                ChangeStatus(PhoneStatus.Talking);
                _soundManager.Stop();
            }
            UpdateHoldStatusUI();
        }

        private void UpdateHoldStatusUI()
        {
            if (_isHolding)
            {
                _holdBtn.Content = "再 開";
                _holdBtn.Background = _holdColor;
                _talkingStatusText.Text = "保留中";
                _talkingStatusText.Foreground = _holdColor;
                if (_talkingIconBg != null) _talkingIconBg.Fill = _holdColor;
                StopAnimation(_talkingPulse); // 保留中は動きを止める
            }
            else
            {
                _holdBtn.Content = "保 留";
                _holdBtn.Background = Brushes.Gray;
                _talkingStatusText.Text = "通話中";
                _talkingStatusText.Foreground = _acceptColor;
                if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;
                StartAnimation(_talkingPulse, 1.2); // 再開で動き出す
            }
        }

        private void EndCall()
        {
            StopAnimation(_talkingPulse);
            StopAnimation(_outgoingPulse);
            StopAnimation(_incomingPulse);
            _callTimer?.Stop();
            _soundManager.Play(SoundManager.FILE_OKI);

            ChangeStatus(PhoneStatus.Idle);

            _viewIncoming.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Collapsed; _viewOutgoing.Visibility = Visibility.Collapsed;
            _viewKeypad.Visibility = Visibility.Visible;
            _inputNumberBox.Text = "";
            _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray;
        }

        private Button CreateDialButton(string t) { var b = new Button { Content = t, FontSize = 24, FontWeight = FontWeights.SemiBold, Background = Brushes.White, Foreground = Brushes.DarkSlateGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var s = new Style(typeof(Border)); s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); b.Resources.Add(typeof(Border), s); b.Click += (o, e) => { _inputNumberBox.Text += t; _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); }; return b; }
        private void OnInputNumberChanged(object s, TextChangedEventArgs e) { string cur = _inputNumberBox.Text; if (string.IsNullOrWhiteSpace(cur)) { _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray; return; } var m = PhoneBook.Entries.FirstOrDefault(x => x.Number == cur); if (m != null) { _statusNameText.Text = m.Name; _statusNameText.Foreground = Brushes.Black; _phoneBookList.SelectedItem = m; _phoneBookList.ScrollIntoView(m); } else { _statusNameText.Text = "未登録の番号"; _statusNameText.Foreground = Brushes.Gray; _phoneBookList.SelectedItem = null; } }
        private void OpenAudioSettings(object sender, RoutedEventArgs e) { var win = new AudioSettingWindow(_currentInputDevice, _currentOutputDevice, _currentInputVol, _currentOutputVol); win.Owner = this; if (win.ShowDialog() == true) { _currentInputDevice = win.SelectedInput; _currentOutputDevice = win.SelectedOutput; _currentInputVol = win.InputVolume; _currentOutputVol = win.OutputVolume; } }
        private void OpenStationSettings(object sender, RoutedEventArgs e) { var win = new StationSelectionWindow(_currentStation); win.Owner = this; if (win.ShowDialog() == true) { _currentStation = win.SelectedStation; Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]"; _selfStationDisplay.Text = $"自局: {_currentStation.Name} ({_currentStation.Number})"; } }
    }
}