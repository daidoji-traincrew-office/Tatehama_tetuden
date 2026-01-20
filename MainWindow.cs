using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using CommunityToolkit.WinUI.Notifications;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RailwayPhone
{
    public enum PhoneStatus { Idle, Incoming, Outgoing, Talking, Holding }

    public class MainWindow : Window
    {
        private CommunicationManager _commManager;
        private UdpVoiceManager _voiceManager;
        private SoundManager _soundManager;

        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_PORT = 8888;

        private DeviceInfo _currentInputDevice;
        private DeviceInfo _normalOutputDevice;
        private DeviceInfo _speakerOutputDevice;
        private float _currentInputVol = 1.0f;
        private float _currentOutputVol = 1.0f;

        private PhoneBookEntry _currentStation;
        private string _connectedTargetNumber; // 現在繋がっている相手の番号
        private string _dialingNumber;         // 発信中の相手の番号

        public PhoneStatus CurrentStatus { get; private set; } = PhoneStatus.Idle;

        // UIパーツ
        private ListView _phoneBookList;
        private TextBlock _selfStationDisplay;
        private Grid _rightPanelContainer;
        private UIElement _viewKeypad, _viewIncoming, _viewOutgoing, _viewTalking;
        private TextBlock _statusNameText, _incomingNameText, _incomingNumberText, _outgoingNameText, _outgoingNumberText, _talkingStatusText, _talkingNameText, _talkingTimerText;
        private TextBox _inputNumberBox;
        private Button _muteBtn, _speakerBtn, _holdBtn;
        private TextBlock _muteBtnLabel, _holdBtnLabel;
        private ScaleTransform _incomingPulse, _outgoingPulse, _talkingPulse;
        private Ellipse _talkingIconBg;

        private DispatcherTimer _callTimer;
        private DateTime _callStartTime;
        private bool _isHolding = false;
        private bool _isMyHold = false;
        private bool _isRemoteHold = false;
        private bool _isMuted = false;
        private bool _isSpeakerOn = false;
        private Random _random = new Random();

        // Colors
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
        private readonly Brush _acceptColor = new SolidColorBrush(Color.FromRgb(30, 180, 50));
        private readonly Brush _holdColor = new SolidColorBrush(Color.FromRgb(255, 140, 0));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));
        private readonly Brush _btnActiveBg = new SolidColorBrush(Colors.White);
        private readonly Brush _btnActiveFg = new SolidColorBrush(Colors.Black);
        private readonly Brush _btnInactiveBg = new SolidColorBrush(Colors.Transparent);
        private readonly Brush _btnInactiveFg = new SolidColorBrush(Colors.Gray);

        public MainWindow(PhoneBookEntry station)
        {
            if (station == null) station = new PhoneBookEntry { Name = "設定なし", Number = "000" };
            _currentStation = station;

            Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]";
            Width = 950; Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bgColor;

            _soundManager = new SoundManager();
            _voiceManager = new UdpVoiceManager(); // 音声はUDPを使用
            _speakerOutputDevice = new DeviceInfo { ID = "-1", Name = "既定のスピーカー" };

            InitializeComponents();

            _commManager = new CommunicationManager();
            SetupSignalREvents(); // SignalRイベント登録
            ConnectToServer();

            Closing += (s, e) => {
                _voiceManager?.Dispose();
                _soundManager?.Dispose();
                _commManager?.Dispose();
                ToastNotificationManagerCompat.Uninstall();
            };
        }

        // SignalRからのイベントを受け取る設定
        private void SetupSignalREvents()
        {
            // 着信 (番号, IP, Port)
            _commManager.IncomingCallReceived += (number, ip, port) =>
                Dispatcher.Invoke(() => HandleIncomingCall(number, ip, port));

            // 相手が応答 (IP, Port)
            _commManager.AnswerReceived += (ip, port) =>
                Dispatcher.Invoke(() => HandleAnswered(ip, port));

            // 切断受信
            _commManager.HangupReceived += (fromNumber) =>
                Dispatcher.Invoke(() =>
                {
                    // 空文字(CANCEL) または 現在の相手からの信号なら切断処理を行う
                    // (他人の切断信号で自分が切れるのを防ぐ)
                    if (string.IsNullOrEmpty(fromNumber) || fromNumber == _connectedTargetNumber)
                    {
                        EndCall(sendSignal: false);
                    }
                });

            // その他の信号
            _commManager.BusyReceived += () => Dispatcher.Invoke(() => HandleBusySignal());
            _commManager.HoldReceived += () => Dispatcher.Invoke(() => HandleRemoteHold(true));
            _commManager.ResumeReceived += () => Dispatcher.Invoke(() => HandleRemoteHold(false));

            // 接続復帰時の処理
            _commManager.Reconnected += () => Dispatcher.Invoke(() => {
                Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}] [オンライン(復帰)]";
                _commManager.SendLogin(_currentStation.Number);
            });
        }

        private async void ConnectToServer()
        {
            Title += " [接続試行中...]";
            bool success = await _commManager.Connect(SERVER_IP, SERVER_PORT);
            Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    _commManager.SendLogin(_currentStation.Number);
                    Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}] [オンライン]";
                }
                else
                {
                    Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}] [オフライン]";
                }
            });
        }

        private void ChangeStatus(PhoneStatus newStatus) { CurrentStatus = newStatus; }

        // --- 発信処理 ---
        private async void StartOutgoingCall()
        {
            string targetNum = _inputNumberBox.Text.Trim();
            if (string.IsNullOrEmpty(targetNum)) return;

            _connectedTargetNumber = targetNum; // 相手番号を記憶
            ChangeStatus(PhoneStatus.Outgoing);

            _viewKeypad.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Collapsed;
            _viewIncoming.Visibility = Visibility.Collapsed; _viewOutgoing.Visibility = Visibility.Visible;

            _outgoingNameText.Text = _statusNameText.Text;
            _outgoingNameText.Foreground = Brushes.Black;
            _outgoingNumberText.Text = targetNum;

            StartAnimation(_outgoingPulse, 0.8);

            if (_normalOutputDevice != null) _soundManager.SetOutputDevice(_normalOutputDevice.ID);
            _soundManager.Play(SoundManager.FILE_TORI);
            await Task.Delay(800);

            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                // SignalRで発信 (自分のUDPポートを伝える)
                _commManager.SendCall(targetNum, _voiceManager.LocalPort);

                // 呼び出し音 (2秒間隔)
                _soundManager.Play(SoundManager.FILE_YOBIDASHI, loop: true, loopIntervalMs: 2000);
            }
        }

        // --- 着信処理 ---
        private void HandleIncomingCall(string fromNumber, string fromIp, int fromPort)
        {
            if (CurrentStatus != PhoneStatus.Idle)
            {
                _commManager.SendBusy(fromNumber);
                return;
            }

            _connectedTargetNumber = fromNumber;

            // 名前解決
            var callerEntry = PhoneBook.Entries.FirstOrDefault(x => x.Number == fromNumber);
            string callerName = callerEntry != null ? callerEntry.Name : "不明な発信者";

            // 応答用に相手情報を保存
            if (_viewIncoming is FrameworkElement fe) fe.Tag = new Tuple<string, int>(fromIp, fromPort);

            ChangeStatus(PhoneStatus.Incoming);
            _viewKeypad.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed; _viewIncoming.Visibility = Visibility.Visible;

            _incomingNameText.Text = callerName;
            _incomingNumberText.Text = fromNumber;
            StartAnimation(_incomingPulse, 0.5);

            if (_normalOutputDevice != null) _soundManager.SetOutputDevice(_normalOutputDevice.ID);

            // 着信音 (1秒間隔)
            if (fromNumber.StartsWith("1")) _soundManager.Play(SoundManager.FILE_YOBI2, loop: true, loopIntervalMs: 1000);
            else _soundManager.Play(SoundManager.FILE_YOBI1, loop: true, loopIntervalMs: 1000);

            new ToastContentBuilder().AddText("着信あり").AddText($"{callerName} ({fromNumber})").Show();
        }

        // --- 応答処理 ---
        private void AnswerCall()
        {
            StopAnimation(_incomingPulse);
            _soundManager.Stop();
            _soundManager.Play(SoundManager.FILE_TORI);

            if ((_viewIncoming as FrameworkElement)?.Tag is Tuple<string, int> info)
            {
                string targetIp = info.Item1;
                int targetPort = info.Item2;

                _commManager.SendAnswer(_connectedTargetNumber, _voiceManager.LocalPort);
                StartVoiceTransmission(targetIp, targetPort, _normalOutputDevice);
            }

            GoToTalkingScreen(isOutgoing: false);
        }

        // --- 相手が応答した ---
        private void HandleAnswered(string ip, int port)
        {
            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                _soundManager.Stop();
                StartVoiceTransmission(ip, port, _normalOutputDevice);
                GoToTalkingScreen(isOutgoing: true);
            }
        }

        private void StartVoiceTransmission(string ip, int port, DeviceInfo outDevice)
        {
            int inDev = -1, outDevId = -1;
            if (_currentInputDevice != null) int.TryParse(_currentInputDevice.ID, out inDev);
            if (outDevice != null) int.TryParse(outDevice.ID, out outDevId);
            _voiceManager.StartTransmission(ip, port, inDev, outDevId);
        }

        private async void HandleBusySignal()
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;

            string busyTarget = _connectedTargetNumber; // 現在の相手を記憶

            _outgoingNameText.Text = "相手は話し中です";
            _outgoingNameText.Foreground = Brushes.Red;
            StopAnimation(_outgoingPulse);
            _soundManager.Stop();
            if (_normalOutputDevice != null) _soundManager.SetOutputDevice(_normalOutputDevice.ID);
            _soundManager.Play(SoundManager.FILE_WATYU);

            await Task.Delay(5000);

            // 5秒後も同じ相手にかけている場合のみ切断
            if (CurrentStatus == PhoneStatus.Outgoing && _connectedTargetNumber == busyTarget)
            {
                EndCall(sendSignal: false, playStatusSound: false);
            }
        }

        private void GoToTalkingScreen(bool isOutgoing)
        {
            ChangeStatus(PhoneStatus.Talking);
            StopAnimation(_outgoingPulse);
            _viewIncoming.Visibility = Visibility.Collapsed; _viewKeypad.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Visible;

            _isMyHold = false; _isRemoteHold = false; _isMuted = false; _isSpeakerOn = false;
            _talkingStatusText.Text = "通話中"; _talkingStatusText.Foreground = _acceptColor;
            if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;
            UpdateButtonVisuals();
            StartAnimation(_talkingPulse, 1.2);

            // 相手名を表示
            if (isOutgoing) _talkingNameText.Text = _outgoingNameText.Text;
            else _talkingNameText.Text = _incomingNameText.Text;

            _callStartTime = DateTime.Now;
            _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _callTimer.Tick += (s, e) => { var span = DateTime.Now - _callStartTime; _talkingTimerText.Text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}"; };
            _callTimer.Start();
        }

        private void ToggleMute()
        {
            _isMuted = !_isMuted;
            if (!_isHolding) _voiceManager.IsMuted = _isMuted;
            UpdateButtonVisuals();
        }

        private void ToggleSpeaker()
        {
            _isSpeakerOn = !_isSpeakerOn;
            DeviceInfo targetDev = _isSpeakerOn ? _speakerOutputDevice : _normalOutputDevice;
            int devId = -1;
            if (targetDev != null) int.TryParse(targetDev.ID, out devId);
            _voiceManager.ChangeOutputDevice(devId);
            UpdateButtonVisuals();
        }

        private void ToggleHold()
        {
            if (_isRemoteHold) return;
            _isMyHold = !_isMyHold;
            if (_isMyHold)
            {
                _commManager.SendHold(_connectedTargetNumber);
                StartHoldState(isSelfInitiated: true);
            }
            else
            {
                _commManager.SendResume(_connectedTargetNumber);
                StopHoldState();
            }
            UpdateButtonVisuals();
        }

        private void HandleRemoteHold(bool isHold)
        {
            _isRemoteHold = isHold;
            if (_isRemoteHold) StartHoldState(isSelfInitiated: false);
            else StopHoldState();
            UpdateButtonVisuals();
        }

        private void StartHoldState(bool isSelfInitiated)
        {
            ChangeStatus(PhoneStatus.Holding);
            _isHolding = true;
            _voiceManager.IsMuted = true;
            int rnd = _random.Next(0, 2);
            string holdFile = (rnd == 0) ? SoundManager.FILE_HOLD1 : SoundManager.FILE_HOLD2;
            _soundManager.Play(holdFile, loop: true);

            if (isSelfInitiated) { _holdBtnLabel.Text = "再 開"; _talkingStatusText.Text = "保留中"; }
            else { _holdBtn.IsEnabled = false; _talkingStatusText.Text = "相手が保留中"; }
            _talkingStatusText.Foreground = _holdColor;
            if (_talkingIconBg != null) _talkingIconBg.Fill = _holdColor;
            StopAnimation(_talkingPulse);
        }

        private void StopHoldState()
        {
            ChangeStatus(PhoneStatus.Talking);
            _isHolding = false;
            _voiceManager.IsMuted = _isMuted;
            _soundManager.Stop();
            _holdBtnLabel.Text = "保 留"; _holdBtn.IsEnabled = true;
            _talkingStatusText.Text = "通話中"; _talkingStatusText.Foreground = _acceptColor;
            if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;
            StartAnimation(_talkingPulse, 1.2);
        }

        private void UpdateButtonVisuals()
        {
            void SetBtnStyle(Button btn, bool isActive, bool isEnabled = true)
            {
                if (btn == null) return;
                btn.IsEnabled = isEnabled;
                btn.Background = isActive ? _btnActiveBg : _btnInactiveBg;
                var stack = btn.Content as StackPanel;
                if (stack != null) foreach (var child in stack.Children) if (child is TextBlock t) t.Foreground = isActive ? _btnActiveFg : _btnInactiveFg;
                btn.BorderBrush = isActive ? Brushes.Transparent : Brushes.LightGray;
            }
            SetBtnStyle(_muteBtn, _isMuted);
            SetBtnStyle(_speakerBtn, _isSpeakerOn);
            if (_holdBtn != null)
            {
                var stack = _holdBtn.Content as StackPanel;
                var text = stack?.Children[1] as TextBlock;
                if (text != null) text.Text = _isMyHold ? "再 開" : "保 留";
            }
            SetBtnStyle(_holdBtn, _isMyHold, !_isRemoteHold);
        }

        private void EndCall(bool sendSignal = true, bool playStatusSound = true)
        {
            _voiceManager.StopTransmission();

            if (sendSignal && !string.IsNullOrEmpty(_connectedTargetNumber))
            {
                _commManager.SendHangup(_connectedTargetNumber);
            }
            _connectedTargetNumber = null;

            StopAnimation(_talkingPulse); StopAnimation(_outgoingPulse); StopAnimation(_incomingPulse);
            _callTimer?.Stop();

            if (playStatusSound)
            {
                if (_normalOutputDevice != null) _soundManager.SetOutputDevice(_normalOutputDevice.ID);
                _soundManager.Play(SoundManager.FILE_OKI);
            }
            else _soundManager.Stop();

            ChangeStatus(PhoneStatus.Idle);
            _viewIncoming.Visibility = Visibility.Collapsed; _viewTalking.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed; _viewKeypad.Visibility = Visibility.Visible;
            _inputNumberBox.Text = "";
            _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray;
        }

        // --- 設定画面呼び出し ---
        private void OpenStationSettings(object sender, RoutedEventArgs e) { var win = new StationSelectionWindow(_currentStation); win.Owner = this; if (win.ShowDialog() == true) { _currentStation = win.SelectedStation; Title = $"模擬鉄 指令電話端末 - [{_currentStation.Name}]"; _selfStationDisplay.Text = $"自局: {_currentStation.Name} ({_currentStation.Number})"; _commManager.SendLogin(_currentStation.Number); Title += " [オンライン(更新)]"; } }
        private void OpenAudioSettings(object sender, RoutedEventArgs e) { var win = new AudioSettingWindow(_currentInputDevice, _normalOutputDevice, _currentInputVol, _currentOutputVol); win.Owner = this; if (win.ShowDialog() == true) { _currentInputDevice = win.SelectedInput; _normalOutputDevice = win.SelectedOutput; _currentInputVol = win.InputVolume; _currentOutputVol = win.OutputVolume; } }

        // --- UI生成コード (変更なし) ---
        private void InitializeComponents() { var dockPanel = new DockPanel(); var menu = new Menu { Background = Brushes.White, Padding = new Thickness(5) }; menu.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 }; DockPanel.SetDock(menu, Dock.Top); var settingsItem = new MenuItem { Header = "設定(_S)" }; var audioItem = new MenuItem { Header = "音声設定(_A)..." }; audioItem.Click += OpenAudioSettings; settingsItem.Items.Add(audioItem); var stationItem = new MenuItem { Header = "自局設定(_M)..." }; stationItem.Click += OpenStationSettings; settingsItem.Items.Add(stationItem); settingsItem.Items.Add(new Separator()); var exitItem = new MenuItem { Header = "終了(_X)" }; exitItem.Click += (s, e) => Close(); settingsItem.Items.Add(exitItem); menu.Items.Add(settingsItem); dockPanel.Children.Add(menu); var mainGrid = new Grid(); mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); var leftPanel = new DockPanel { Margin = new Thickness(15, 15, 5, 15) }; var listHeader = new TextBlock { Text = "連絡先リスト", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(5, 0, 0, 10) }; DockPanel.SetDock(listHeader, Dock.Top); _phoneBookList = new ListView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch }; ScrollViewer.SetHorizontalScrollBarVisibility(_phoneBookList, ScrollBarVisibility.Disabled); var itemTemplate = new DataTemplate(); var fb = new FrameworkElementFactory(typeof(Border)); fb.SetValue(Border.BackgroundProperty, Brushes.White); fb.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); fb.SetValue(Border.PaddingProperty, new Thickness(10)); fb.SetValue(Border.MarginProperty, new Thickness(2, 0, 5, 8)); fb.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 }); var fg = new FrameworkElementFactory(typeof(Grid)); var c1 = new FrameworkElementFactory(typeof(ColumnDefinition)); c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(40)); var c2 = new FrameworkElementFactory(typeof(ColumnDefinition)); c2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star)); var c3 = new FrameworkElementFactory(typeof(ColumnDefinition)); c3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto); fg.AppendChild(c1); fg.AppendChild(c2); fg.AppendChild(c3); var fe = new FrameworkElementFactory(typeof(Ellipse)); fe.SetValue(Ellipse.WidthProperty, 32.0); fe.SetValue(Ellipse.HeightProperty, 32.0); fe.SetValue(Ellipse.FillProperty, Brushes.WhiteSmoke); fe.SetValue(Grid.ColumnProperty, 0); fe.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); fg.AppendChild(fe); var fit = new FrameworkElementFactory(typeof(TextBlock)); fit.SetValue(TextBlock.TextProperty, "📞"); fit.SetValue(TextBlock.FontSizeProperty, 14.0); fit.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); fit.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); fit.SetValue(Grid.ColumnProperty, 0); fg.AppendChild(fit); var fn = new FrameworkElementFactory(typeof(TextBlock)); fn.SetBinding(TextBlock.TextProperty, new Binding("Name")); fn.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); fn.SetValue(TextBlock.FontSizeProperty, 13.0); fn.SetValue(TextBlock.ForegroundProperty, Brushes.Black); fn.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); fn.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0)); fn.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); fn.SetValue(Grid.ColumnProperty, 1); fg.AppendChild(fn); var fnum = new FrameworkElementFactory(typeof(TextBlock)); fnum.SetBinding(TextBlock.TextProperty, new Binding("Number")); fnum.SetValue(TextBlock.FontSizeProperty, 16.0); fnum.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas")); fnum.SetValue(TextBlock.ForegroundProperty, _primaryColor); fnum.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); fnum.SetValue(Grid.ColumnProperty, 2); fg.AppendChild(fnum); fb.AppendChild(fg); itemTemplate.VisualTree = fb; _phoneBookList.ItemTemplate = itemTemplate; _phoneBookList.ItemsSource = PhoneBook.Entries; _phoneBookList.SelectionChanged += (s, e) => { var sel = _phoneBookList.SelectedItem as PhoneBookEntry; if (sel != null) _inputNumberBox.Text = sel.Number; }; leftPanel.Children.Add(_phoneBookList); Grid.SetColumn(leftPanel, 0); mainGrid.Children.Add(leftPanel); var rightWrapper = new Grid { Margin = new Thickness(5, 15, 15, 15) }; var rightCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 10 } }; rightWrapper.Children.Add(rightCard); _rightPanelContainer = new Grid(); rightCard.Child = _rightPanelContainer; _viewKeypad = CreateKeypadView(); _rightPanelContainer.Children.Add(_viewKeypad); _viewIncoming = CreateIncomingView(); _viewIncoming.Visibility = Visibility.Collapsed; _rightPanelContainer.Children.Add(_viewIncoming); _viewOutgoing = CreateOutgoingView(); _viewOutgoing.Visibility = Visibility.Collapsed; _rightPanelContainer.Children.Add(_viewOutgoing); _viewTalking = CreateTalkingView(); _viewTalking.Visibility = Visibility.Collapsed; _rightPanelContainer.Children.Add(_viewTalking); Grid.SetColumn(rightWrapper, 1); mainGrid.Children.Add(rightWrapper); dockPanel.Children.Add(mainGrid); Content = dockPanel; }
        private Grid CreatePulsingIcon(Brush color, out ScaleTransform transform) { var g = new Grid { Width = 100, Height = 100, Margin = new Thickness(0, 0, 0, 20) }; var el = new Ellipse { Fill = color, Opacity = 0.2 }; var tx = new TextBlock { Text = "📞", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = color }; transform = new ScaleTransform(1.0, 1.0, 50, 50); el.RenderTransform = transform; g.Children.Add(el); g.Children.Add(tx); return g; }
        private UIElement CreateKeypadView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 280 }; _selfStationDisplay = new TextBlock { Text = $"自局: {_currentStation.Name} ({_currentStation.Number})", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30), Background = new SolidColorBrush(Color.FromRgb(230, 240, 255)), Padding = new Thickness(10, 5, 10, 5) }; p.Children.Add(_selfStationDisplay); _statusNameText = new TextBlock { Text = "宛先未指定", FontSize = 20, FontWeight = FontWeights.Light, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) }; p.Children.Add(_statusNameText); _inputNumberBox = new TextBox { Text = "", FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalContentAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = _primaryColor, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 30), FontFamily = new FontFamily("Consolas") }; _inputNumberBox.TextChanged += OnInputNumberChanged; p.Children.Add(_inputNumberBox); var kg = new Grid { Margin = new Thickness(0, 0, 0, 30), HorizontalAlignment = HorizontalAlignment.Center }; for (int i = 0; i < 3; i++) kg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); for (int i = 0; i < 4; i++) kg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) }); int num = 1; for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) { var btn = CreateDialButton(num.ToString()); Grid.SetRow(btn, row); Grid.SetColumn(btn, col); kg.Children.Add(btn); num++; } } var bs = CreateDialButton("*"); Grid.SetRow(bs, 3); Grid.SetColumn(bs, 0); kg.Children.Add(bs); var bz = CreateDialButton("0"); Grid.SetRow(bz, 3); Grid.SetColumn(bz, 1); kg.Children.Add(bz); var bd = new Button { Content = "⌫", FontSize = 20, FontWeight = FontWeights.Bold, Background = Brushes.WhiteSmoke, Foreground = Brushes.DimGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var ds = new Style(typeof(Border)); ds.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); bd.Resources.Add(typeof(Border), ds); bd.Click += (s, e) => { var t = _inputNumberBox.Text; if (!string.IsNullOrEmpty(t)) { _inputNumberBox.Text = t.Substring(0, t.Length - 1); _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); } }; Grid.SetRow(bd, 3); Grid.SetColumn(bd, 2); kg.Children.Add(bd); p.Children.Add(kg); var cb = new Button { Content = "発 信", Height = 50, FontSize = 18, FontWeight = FontWeights.Bold, Background = _primaryColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(10, 0, 10, 0) }; var cs = new Style(typeof(Border)); cs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(25))); cb.Resources.Add(typeof(Border), cs); cb.Click += (s, e) => StartOutgoingCall(); p.Children.Add(cb); return p; }
        private UIElement CreateIncomingView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; p.Children.Add(new TextBlock { Text = "着信中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }); p.Children.Add(CreatePulsingIcon(_primaryColor, out _incomingPulse)); _incomingNameText = new TextBlock { Text = "---", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(_incomingNameText); _incomingNumberText = new TextBlock { Text = "---", FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) }; p.Children.Add(_incomingNumberText); var bg = new Grid { Width = 300 }; bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); var ab = new Button { Content = "📞 応答", Height = 60, Background = _acceptColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var @as = new Style(typeof(Border)); @as.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); ab.Resources.Add(typeof(Border), @as); ab.Click += (s, e) => AnswerCall(); Grid.SetColumn(ab, 0); bg.Children.Add(ab); var rb = new Button { Content = "切断", Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var rs = new Style(typeof(Border)); rs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); rb.Resources.Add(typeof(Border), rs); rb.Click += (s, e) => EndCall(false); Grid.SetColumn(rb, 2); bg.Children.Add(rb); p.Children.Add(bg); return p; }
        private UIElement CreateOutgoingView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; p.Children.Add(new TextBlock { Text = "呼び出し中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }); p.Children.Add(CreatePulsingIcon(_primaryColor, out _outgoingPulse)); _outgoingNameText = new TextBlock { Text = "---", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(_outgoingNameText); _outgoingNumberText = new TextBlock { Text = "---", FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) }; p.Children.Add(_outgoingNumberText); var cb = new Button { Content = "取 消", Width = 200, Height = 60, Background = _dangerColor, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand }; var cs = new Style(typeof(Border)); cs.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); cb.Resources.Add(typeof(Border), cs); cb.Click += (s, e) => EndCall(true); p.Children.Add(cb); return p; }
        private UIElement CreateTalkingView() { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; var ip = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; var ig = CreatePulsingIcon(_acceptColor, out _talkingPulse); _talkingIconBg = ig.Children.OfType<Ellipse>().FirstOrDefault(); ip.Children.Add(ig); _talkingStatusText = new TextBlock { Text = "通話中", FontSize = 16, Foreground = _acceptColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 5) }; ip.Children.Add(_talkingStatusText); _talkingNameText = new TextBlock { Text = "---", FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brushes.Black, Margin = new Thickness(0, 0, 0, 5) }; ip.Children.Add(_talkingNameText); _talkingTimerText = new TextBlock { Text = "00:00", FontSize = 20, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center }; ip.Children.Add(_talkingTimerText); p.Children.Add(ip); var bg = new Grid { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center }; for (int i = 0; i < 3; i++) bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Button CreateControlBtn(string icon, string text, RoutedEventHandler handler, out Button btnRef, out TextBlock labelRef) { var btn = new Button { Background = Brushes.Transparent, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(10), Cursor = System.Windows.Input.Cursors.Hand, Height = 80, Width = 80 }; var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var ico = new TextBlock { Text = icon, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray }; var lbl = new TextBlock { Text = text, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) }; stack.Children.Add(ico); stack.Children.Add(lbl); btn.Content = stack; var style = new Style(typeof(Border)); style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(40))); btn.Resources.Add(typeof(Border), style); btn.Click += handler; btnRef = btn; labelRef = lbl; return btn; } var b1 = CreateControlBtn("🔇", "ミュート", (s, e) => ToggleMute(), out _muteBtn, out _muteBtnLabel); Grid.SetColumn(b1, 0); bg.Children.Add(b1); var b2 = CreateControlBtn("🔊", "スピーカー", (s, e) => ToggleSpeaker(), out _speakerBtn, out _muteBtnLabel); Grid.SetColumn(b2, 1); bg.Children.Add(b2); var b3 = CreateControlBtn("⏸", "保 留", (s, e) => ToggleHold(), out _holdBtn, out _holdBtnLabel); Grid.SetColumn(b3, 2); bg.Children.Add(b3); p.Children.Add(bg); var eb = new Button { Content = "📞", FontSize = 32, Foreground = Brushes.White, Width = 80, Height = 80, Background = _dangerColor, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 0, 20) }; var es = new Style(typeof(Border)); es.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(40))); eb.Resources.Add(typeof(Border), es); eb.Click += (s, e) => EndCall(true); p.Children.Add(eb); return p; }
        private void StartAnimation(ScaleTransform target, double durationSec) { if (target == null) return; var ax = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }; var ay = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }; target.BeginAnimation(ScaleTransform.ScaleXProperty, ax); target.BeginAnimation(ScaleTransform.ScaleYProperty, ay); }
        private void StopAnimation(ScaleTransform target) { if (target == null) return; target.BeginAnimation(ScaleTransform.ScaleXProperty, null); target.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        private Button CreateDialButton(string t) { var b = new Button { Content = t, FontSize = 24, FontWeight = FontWeights.SemiBold, Background = Brushes.White, Foreground = Brushes.DarkSlateGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand }; var s = new Style(typeof(Border)); s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); b.Resources.Add(typeof(Border), s); b.Click += (o, e) => { _inputNumberBox.Text += t; _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); }; return b; }
        private void OnInputNumberChanged(object s, TextChangedEventArgs e) { string cur = _inputNumberBox.Text; if (string.IsNullOrWhiteSpace(cur)) { _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray; return; } var m = PhoneBook.Entries.FirstOrDefault(x => x.Number == cur); if (m != null) { _statusNameText.Text = m.Name; _statusNameText.Foreground = Brushes.Black; _phoneBookList.SelectedItem = m; _phoneBookList.ScrollIntoView(m); } else { _statusNameText.Text = "未登録の番号"; _statusNameText.Foreground = Brushes.Gray; _phoneBookList.SelectedItem = null; } }
    }
}