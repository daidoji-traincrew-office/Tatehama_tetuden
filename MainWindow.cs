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
        // サーバー設定
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_PORT = 8888;
        private const int SERVER_GRPC_PORT = 8889;

        // マネージャー
        private CommunicationManager _commManager;
        private GrpcVoiceManager _voiceManager;
        private SoundManager _soundManager;

        // デバイス・設定
        private DeviceInfo _currentInputDevice;
        private DeviceInfo _normalOutputDevice;
        private DeviceInfo _speakerOutputDevice;
        private float _currentInputVol = 1.0f;
        private float _currentOutputVol = 1.0f;
        private PhoneBookEntry _currentStation;

        // ID管理
        private string _myConnectionId;
        private string _targetConnectionId;
        private string _connectedTargetNumber;

        public PhoneStatus CurrentStatus { get; private set; } = PhoneStatus.Idle;

        // ユーティリティ
        private DispatcherTimer _callTimer;
        private DateTime _callStartTime;
        private Random _random = new Random();

        // フラグ
        private bool _isHolding = false;
        private bool _isMyHold = false;
        private bool _isRemoteHold = false;
        private bool _isMuted = false;
        private bool _isSpeakerOn = false;

        // UIコンポーネント
        private ListView _phoneBookList;
        private TextBlock _selfStationDisplay;
        private Grid _rightPanelContainer;
        private UIElement _viewKeypad, _viewIncoming, _viewOutgoing, _viewTalking;
        private TextBlock _statusNameText, _incomingNameText, _incomingNumberText, _outgoingNameText, _outgoingNumberText, _talkingStatusText, _talkingNameText, _talkingTimerText;
        private TextBox _inputNumberBox;
        private Button _muteBtn, _speakerBtn, _holdBtn;
        private TextBlock _muteBtnLabel, _holdBtnLabel;

        // ★追加: 色を変更するために発信ボタンをフィールド化
        private Button _callBtn;

        private ScaleTransform _incomingPulse, _outgoingPulse, _talkingPulse;
        private Ellipse _talkingIconBg;

        // デザイン定数
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
        private readonly Brush _acceptColor = new SolidColorBrush(Color.FromRgb(30, 180, 50));
        private readonly Brush _holdColor = new SolidColorBrush(Color.FromRgb(255, 140, 0));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));
        private readonly Brush _offlineBgColor = new SolidColorBrush(Color.FromRgb(220, 220, 220));
        private readonly Brush _warningBgColor = new SolidColorBrush(Color.FromRgb(255, 240, 240));
        private readonly Brush _btnActiveBg = new SolidColorBrush(Colors.White);
        private readonly Brush _btnActiveFg = new SolidColorBrush(Colors.Black);
        private readonly Brush _btnInactiveBg = new SolidColorBrush(Colors.Transparent);
        private readonly Brush _btnInactiveFg = new SolidColorBrush(Colors.Gray);

        public MainWindow(PhoneBookEntry station)
        {
            if (station == null) station = new PhoneBookEntry { Name = "設定なし", Number = "000" };
            _currentStation = station;

            Title = $"館浜電鉄 鉄道電話 - [{_currentStation.Name}]";
            Width = 950; Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bgColor;

            _soundManager = new SoundManager();
            _voiceManager = new GrpcVoiceManager();
            _speakerOutputDevice = new DeviceInfo { ID = "-1", Name = "既定のスピーカー" };

            InitializeComponents();

            _commManager = new CommunicationManager();
            SetupSignalREvents();
            ConnectToServer();

            Closing += (s, e) => {
                _voiceManager?.Dispose();
                _soundManager?.Dispose();
                _commManager?.Dispose();
                ToastNotificationManagerCompat.Uninstall();
            };
        }

        private void SetupSignalREvents()
        {
            _commManager.LoginSuccess += (id) => _myConnectionId = id;
            _commManager.IncomingCallReceived += (number, callerId) => Dispatcher.Invoke(() => HandleIncomingCall(number, callerId));
            _commManager.AnswerReceived += (responderId) => Dispatcher.Invoke(() => HandleAnswered(responderId));

            _commManager.HangupReceived += (fromId) => Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(fromId) || fromId == _targetConnectionId) EndCall(sendSignal: false);
            });

            _commManager.CancelReceived += (fromId) => Dispatcher.Invoke(() =>
            {
                if (CurrentStatus == PhoneStatus.Incoming && fromId == _targetConnectionId) EndCall(sendSignal: false, playSound: false);
            });

            _commManager.RejectReceived += (fromId) => Dispatcher.Invoke(() => HandleRejected());

            _commManager.BusyReceived += () => Dispatcher.Invoke(() => HandleBusySignal());
            _commManager.HoldReceived += () => Dispatcher.Invoke(() => HandleRemoteHold(true));
            _commManager.ResumeReceived += () => Dispatcher.Invoke(() => HandleRemoteHold(false));

            _commManager.ConnectionLost += () => Dispatcher.Invoke(() => SetOfflineState());
            _commManager.Reconnecting += () => Dispatcher.Invoke(() => { Title += " [🔄]"; Background = _warningBgColor; });
            _commManager.Reconnected += () => Dispatcher.Invoke(() => { SetOnlineState(); _commManager.SendLogin(_currentStation.Number); });
        }

        private async void ConnectToServer()
        {
            Title += " [接続試行中...]";
            bool success = await _commManager.Connect(SERVER_IP, SERVER_PORT);
            Dispatcher.Invoke(() => {
                if (success) { _commManager.SendLogin(_currentStation.Number); SetOnlineState(); }
                else { MessageBox.Show("サーバー接続失敗"); SetOfflineState(); }
            });
        }

        private void SetOfflineState() { Title = $"[圏外] {_currentStation.Name}"; Background = _offlineBgColor; if (_selfStationDisplay != null) { _selfStationDisplay.Text = "圏外"; _selfStationDisplay.Foreground = Brushes.Gray; _selfStationDisplay.Background = Brushes.Transparent; } }
        private void SetOnlineState() { Title = $"館浜電鉄 鉄道電話 - [{_currentStation.Name}]"; Background = _bgColor; if (_selfStationDisplay != null) { _selfStationDisplay.Text = $"自局: {_currentStation.Name} ({_currentStation.Number})"; _selfStationDisplay.Foreground = _primaryColor; _selfStationDisplay.Background = new SolidColorBrush(Color.FromRgb(230, 240, 255)); } }
        private void ChangeStatus(PhoneStatus s) => CurrentStatus = s;

        // --- 通話ロジック ---

        private async void StartOutgoingCall()
        {
            string targetNum = _inputNumberBox.Text.Trim();
            if (string.IsNullOrEmpty(targetNum)) return;
            _connectedTargetNumber = targetNum;
            ChangeStatus(PhoneStatus.Outgoing);
            SwitchView(_viewOutgoing);
            _outgoingNumberText.Text = targetNum;

            if (!_commManager.IsConnected)
            {
                _outgoingNameText.Text = "圏外です"; _outgoingNameText.Foreground = Brushes.Red;
                _soundManager.Play(SoundManager.FILE_WATYU);
                await Task.Delay(3000);
                if (CurrentStatus == PhoneStatus.Outgoing) EndCall(false, false);
                return;
            }

            _outgoingNameText.Text = _statusNameText.Text; _outgoingNameText.Foreground = Brushes.Black;
            StartAnimation(_outgoingPulse, 0.8);
            _soundManager.Play(SoundManager.FILE_TORI);
            await Task.Delay(800);

            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                _commManager.SendCall(targetNum);
                _soundManager.Play(SoundManager.FILE_YOBIDASHI, loop: true, loopIntervalMs: 2000);
            }
        }

        private void HandleIncomingCall(string fromNumber, string callerId)
        {
            if (CurrentStatus != PhoneStatus.Idle) { _commManager.SendBusy(callerId); return; }
            _connectedTargetNumber = fromNumber;
            _targetConnectionId = callerId;

            var entry = PhoneBook.Entries.FirstOrDefault(x => x.Number == fromNumber);
            string name = entry != null ? entry.Name : "不明";

            if (_viewIncoming is FrameworkElement fe) fe.Tag = new Tuple<string, int>("", 0);

            ChangeStatus(PhoneStatus.Incoming); SwitchView(_viewIncoming);
            _incomingNameText.Text = name; _incomingNumberText.Text = fromNumber;
            StartAnimation(_incomingPulse, 0.5);
            _soundManager.Play(SoundManager.FILE_YOBI1, loop: true, loopIntervalMs: 1000);
            new ToastContentBuilder().AddText("着信あり").AddText($"{name} ({fromNumber})").Show();
        }

        private void AnswerCall()
        {
            StopAnimation(_incomingPulse); _soundManager.Stop(); _soundManager.Play(SoundManager.FILE_TORI);
            _commManager.SendAnswer(_connectedTargetNumber, _targetConnectionId);
            StartVoiceTransmission(_targetConnectionId);
            GoToTalkingScreen(false);
        }

        private void HandleAnswered(string responderId)
        {
            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                _soundManager.Stop();
                _targetConnectionId = responderId;
                StartVoiceTransmission(_targetConnectionId);
                GoToTalkingScreen(true);
            }
        }

        private void StartVoiceTransmission(string targetId)
        {
            int inDev = -1, outDevId = -1;
            if (_currentInputDevice != null) int.TryParse(_currentInputDevice.ID, out inDev);
            if (_normalOutputDevice != null) int.TryParse(_normalOutputDevice.ID, out outDevId);
            _voiceManager.StartTransmission(_myConnectionId, targetId, SERVER_IP, SERVER_GRPC_PORT, inDev, outDevId);
        }

        private async void HandleBusySignal()
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;
            _outgoingNameText.Text = "話中"; _outgoingNameText.Foreground = Brushes.Red;
            StopAnimation(_outgoingPulse); _soundManager.Play(SoundManager.FILE_WATYU);
            await Task.Delay(5000);
            if (CurrentStatus == PhoneStatus.Outgoing) EndCall(false, false);
        }

        private async void HandleRejected()
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;
            _outgoingNameText.Text = "事情によりお繋ぎできません"; _outgoingNameText.Foreground = Brushes.Red;
            StopAnimation(_outgoingPulse);
            _soundManager.Stop();
            if (_normalOutputDevice != null) _soundManager.SetOutputDevice(_normalOutputDevice.ID);
            _soundManager.Play(SoundManager.FILE_WATYU);
            await Task.Delay(5000);
            if (CurrentStatus == PhoneStatus.Outgoing) EndCall(false, false);
        }

        private void EndCall(bool sendSignal = true, bool playSound = true)
        {
            // ★修正: 着信中の切断は「着信拒否」として送信 (true)
            if (CurrentStatus == PhoneStatus.Incoming && sendSignal && !string.IsNullOrEmpty(_targetConnectionId))
            {
                _commManager.SendReject(_targetConnectionId);
            }
            else if (sendSignal && !string.IsNullOrEmpty(_targetConnectionId))
            {
                _commManager.SendHangup(_targetConnectionId);
            }

            _voiceManager.StopTransmission();
            _connectedTargetNumber = null; _targetConnectionId = null;
            StopAnimation(_talkingPulse); StopAnimation(_outgoingPulse); StopAnimation(_incomingPulse);
            _callTimer?.Stop();
            if (playSound)
            {
                if (_normalOutputDevice != null) _soundManager.SetOutputDevice(_normalOutputDevice.ID);
                _soundManager.Play(SoundManager.FILE_OKI);
            }
            else _soundManager.Stop();

            ChangeStatus(PhoneStatus.Idle); SwitchView(_viewKeypad);
            _inputNumberBox.Text = ""; _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray;

            // ★リセット時に発信ボタンを青に戻す
            if (_callBtn != null) _callBtn.Background = _primaryColor;
        }

        // --- 通話中機能 ---

        private void GoToTalkingScreen(bool isOutgoing)
        {
            ChangeStatus(PhoneStatus.Talking); SwitchView(_viewTalking); StopAnimation(_outgoingPulse);
            _isMyHold = _isRemoteHold = _isMuted = _isSpeakerOn = false;

            _talkingStatusText.Text = "通話中";
            _talkingStatusText.Foreground = _acceptColor;
            if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;

            UpdateButtonVisuals();
            StartAnimation(_talkingPulse, 1.2);

            if (isOutgoing) _talkingNameText.Text = _outgoingNameText.Text;
            else _talkingNameText.Text = _incomingNameText.Text;

            _callStartTime = DateTime.Now;
            _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _callTimer.Tick += (s, e) => _talkingTimerText.Text = DateTime.Now.Subtract(_callStartTime).ToString(@"mm\:ss");
            _callTimer.Start();
        }

        private void ToggleMute() { _isMuted = !_isMuted; if (!_isHolding) _voiceManager.IsMuted = _isMuted; UpdateButtonVisuals(); }
        private void ToggleSpeaker()
        {
            _isSpeakerOn = !_isSpeakerOn;
            int id = -1; if (_isSpeakerOn && _speakerOutputDevice != null) int.TryParse(_speakerOutputDevice.ID, out id);
            else if (!_isSpeakerOn && _normalOutputDevice != null) int.TryParse(_normalOutputDevice.ID, out id);
            _voiceManager.ChangeOutputDevice(id); UpdateButtonVisuals();
        }
        private void ToggleHold()
        {
            if (_isRemoteHold) return;
            _isMyHold = !_isMyHold;
            if (_isMyHold) { _commManager.SendHold(_targetConnectionId); StartHoldState(true); }
            else { _commManager.SendResume(_targetConnectionId); StopHoldState(); }
            UpdateButtonVisuals();
        }
        private void HandleRemoteHold(bool isHold) { _isRemoteHold = isHold; if (isHold) StartHoldState(false); else StopHoldState(); UpdateButtonVisuals(); }

        private void StartHoldState(bool self)
        {
            ChangeStatus(PhoneStatus.Holding); _isHolding = true; _voiceManager.IsMuted = true;
            _soundManager.Play(SoundManager.FILE_HOLD1, true);
            _talkingStatusText.Text = self ? "保留中" : "相手が保留"; StopAnimation(_talkingPulse);
        }
        private void StopHoldState()
        {
            ChangeStatus(PhoneStatus.Talking); _isHolding = false; _voiceManager.IsMuted = _isMuted;
            _soundManager.Stop(); _talkingStatusText.Text = "通話中"; StartAnimation(_talkingPulse, 1.2);
        }

        private void UpdateButtonVisuals()
        {
            void Set(Button b, bool active) { if (b == null) return; b.Background = active ? _btnActiveBg : _btnInactiveBg; }
            Set(_muteBtn, _isMuted); Set(_speakerBtn, _isSpeakerOn);
            if (_holdBtn != null)
            {
                var stack = _holdBtn.Content as StackPanel;
                var text = stack?.Children[1] as TextBlock;
                if (text != null) text.Text = _isMyHold ? "再 開" : "保 留";
            }
            Set(_holdBtn, _isMyHold);
        }

        // --- ヘルパーメソッド ---

        private Grid CreatePulsingIcon(Brush color, out ScaleTransform transform)
        {
            var g = new Grid { Width = 100, Height = 100, Margin = new Thickness(0, 0, 0, 20) };
            var el = new Ellipse { Fill = color, Opacity = 0.2 };
            var tx = new TextBlock { Text = "📞", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = color };
            transform = new ScaleTransform(1.0, 1.0, 50, 50);
            el.RenderTransform = transform;
            g.Children.Add(el); g.Children.Add(tx);
            return g;
        }

        // アイコン生成ヘルパー (位置調整版)
        private object GetPhoneIcon(bool isHangUp)
        {
            var grid = new Grid { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock
            {
                Text = "📞",
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                TextAlignment = TextAlignment.Center
            };

            if (isHangUp)
            {
                tb.RenderTransform = new RotateTransform(135);
                tb.Margin = new Thickness(0, 5, 0, 0);
            }

            grid.Children.Add(tb);
            return grid;
        }

        private void SwitchView(UIElement visibleView)
        {
            _viewKeypad.Visibility = Visibility.Collapsed;
            _viewTalking.Visibility = Visibility.Collapsed;
            _viewIncoming.Visibility = Visibility.Collapsed;
            _viewOutgoing.Visibility = Visibility.Collapsed;
            visibleView.Visibility = Visibility.Visible;
        }

        private void StartAnimation(ScaleTransform target, double durationSec)
        {
            if (target == null) return;
            var ax = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var ay = new DoubleAnimation(1.0, 1.2, TimeSpan.FromSeconds(durationSec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            target.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }

        private void StopAnimation(ScaleTransform target)
        {
            if (target == null) return;
            target.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void OpenStationSettings(object sender, RoutedEventArgs e)
        {
            var win = new StationSelectionWindow(_currentStation);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _currentStation = win.SelectedStation;
                if (_commManager.IsConnected) { SetOnlineState(); _commManager.SendLogin(_currentStation.Number); } else SetOfflineState();
            }
        }

        private void OpenAudioSettings(object sender, RoutedEventArgs e)
        {
            var win = new AudioSettingWindow(_currentInputDevice, _normalOutputDevice, _currentInputVol, _currentOutputVol);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _currentInputDevice = win.SelectedInput;
                _normalOutputDevice = win.SelectedOutput;
                _currentInputVol = win.InputVolume;
                _currentOutputVol = win.OutputVolume;
            }
        }

        private void OnInputNumberChanged(object s, TextChangedEventArgs e)
        {
            string cur = _inputNumberBox.Text;
            if (string.IsNullOrWhiteSpace(cur)) { _statusNameText.Text = "宛先未指定"; _statusNameText.Foreground = Brushes.Gray; }
            else
            {
                var m = PhoneBook.Entries.FirstOrDefault(x => x.Number == cur);
                if (m != null) { _statusNameText.Text = m.Name; _statusNameText.Foreground = Brushes.Black; _phoneBookList.SelectedItem = m; _phoneBookList.ScrollIntoView(m); }
                else { _statusNameText.Text = "未登録の番号"; _statusNameText.Foreground = Brushes.Gray; _phoneBookList.SelectedItem = null; }
            }

            // ★追加: 番号が入力されたら発信ボタンを緑色にする
            if (_callBtn != null)
            {
                _callBtn.Background = cur.Length > 0 ? _acceptColor : _primaryColor;
            }
        }

        // --- UI生成コード ---

        private void InitializeComponents()
        {
            var dockPanel = new DockPanel();
            var menu = new Menu { Background = Brushes.White, Padding = new Thickness(5) };
            menu.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 };
            DockPanel.SetDock(menu, Dock.Top);

            var settingsItem = new MenuItem { Header = "設定(_S)" };
            var audioItem = new MenuItem { Header = "音声設定(_A)..." };
            audioItem.Click += OpenAudioSettings;
            settingsItem.Items.Add(audioItem);
            var stationItem = new MenuItem { Header = "自局設定(_M)..." };
            stationItem.Click += OpenStationSettings;
            settingsItem.Items.Add(stationItem);
            settingsItem.Items.Add(new Separator());
            var exitItem = new MenuItem { Header = "終了(_X)" };
            exitItem.Click += (s, e) => Close();
            settingsItem.Items.Add(exitItem);
            menu.Items.Add(settingsItem);
            dockPanel.Children.Add(menu);

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = new DockPanel { Margin = new Thickness(15, 15, 5, 15) };
            var listHeader = new TextBlock { Text = "連絡先リスト", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(5, 0, 0, 10) };
            DockPanel.SetDock(listHeader, Dock.Top);

            _phoneBookList = new ListView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(_phoneBookList, ScrollBarVisibility.Disabled);

            // --- ListView Template (復旧) ---
            var itemTemplate = new DataTemplate();
            var fb = new FrameworkElementFactory(typeof(Border));
            fb.SetValue(Border.BackgroundProperty, Brushes.White);
            fb.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            fb.SetValue(Border.PaddingProperty, new Thickness(10));
            fb.SetValue(Border.MarginProperty, new Thickness(2, 0, 5, 8));
            fb.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 });

            var fg = new FrameworkElementFactory(typeof(Grid));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition)); c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(40));
            var c2 = new FrameworkElementFactory(typeof(ColumnDefinition)); c2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c3 = new FrameworkElementFactory(typeof(ColumnDefinition)); c3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            fg.AppendChild(c1); fg.AppendChild(c2); fg.AppendChild(c3);

            var fe = new FrameworkElementFactory(typeof(Ellipse));
            fe.SetValue(Ellipse.WidthProperty, 32.0); fe.SetValue(Ellipse.HeightProperty, 32.0);
            fe.SetValue(Ellipse.FillProperty, Brushes.WhiteSmoke);
            fe.SetValue(Grid.ColumnProperty, 0);
            fe.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            fg.AppendChild(fe);

            var fit = new FrameworkElementFactory(typeof(TextBlock));
            fit.SetValue(TextBlock.TextProperty, "📞");
            fit.SetValue(TextBlock.FontSizeProperty, 14.0);
            fit.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            fit.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            fit.SetValue(Grid.ColumnProperty, 0);
            fg.AppendChild(fit);

            var fn = new FrameworkElementFactory(typeof(TextBlock));
            fn.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            fn.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            fn.SetValue(TextBlock.FontSizeProperty, 13.0);
            fn.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            fn.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            fn.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0));
            fn.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            fn.SetValue(Grid.ColumnProperty, 1);
            fg.AppendChild(fn);

            var fnum = new FrameworkElementFactory(typeof(TextBlock));
            fnum.SetBinding(TextBlock.TextProperty, new Binding("Number"));
            fnum.SetValue(TextBlock.FontSizeProperty, 16.0);
            fnum.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
            fnum.SetValue(TextBlock.ForegroundProperty, _primaryColor);
            fnum.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            fnum.SetValue(Grid.ColumnProperty, 2);
            fg.AppendChild(fnum);

            fb.AppendChild(fg);
            itemTemplate.VisualTree = fb;
            _phoneBookList.ItemTemplate = itemTemplate;
            _phoneBookList.ItemsSource = PhoneBook.Entries;
            _phoneBookList.SelectionChanged += (s, e) => {
                var sel = _phoneBookList.SelectedItem as PhoneBookEntry;
                if (sel != null) _inputNumberBox.Text = sel.Number;
            };

            leftPanel.Children.Add(_phoneBookList);
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            var rightWrapper = new Grid { Margin = new Thickness(5, 15, 15, 15) };
            var rightCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 10 } };
            rightWrapper.Children.Add(rightCard);
            _rightPanelContainer = new Grid();
            rightCard.Child = _rightPanelContainer;

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

            Grid.SetColumn(rightWrapper, 1);
            mainGrid.Children.Add(rightWrapper);
            dockPanel.Children.Add(mainGrid);
            Content = dockPanel;
        }

        private UIElement CreateKeypadView()
        {
            var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 280 };

            _selfStationDisplay = new TextBlock { Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30), FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(230, 240, 255)), Padding = new Thickness(10, 5, 10, 5) }; p.Children.Add(_selfStationDisplay);
            _statusNameText = new TextBlock { Text = "宛先未指定", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Light, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 20) }; p.Children.Add(_statusNameText);
            _inputNumberBox = new TextBox { FontSize = 36, HorizontalContentAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = _primaryColor, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 30) }; _inputNumberBox.TextChanged += OnInputNumberChanged; p.Children.Add(_inputNumberBox);

            var kg = new Grid { Margin = new Thickness(0, 0, 0, 30), HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 3; i++) kg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            for (int i = 0; i < 4; i++) kg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            int n = 1; for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) { var b = CreateDialButton(n++.ToString()); Grid.SetRow(b, r); Grid.SetColumn(b, c); kg.Children.Add(b); }
            var b0 = CreateDialButton("0"); Grid.SetRow(b0, 3); Grid.SetColumn(b0, 1); kg.Children.Add(b0);

            // ★修正: 取消ボタンを「⌫」に戻し、丸いスタイルを適用
            var bBS = new Button { Content = "⌫", FontSize = 24, FontWeight = FontWeights.Bold, Background = Brushes.WhiteSmoke, Foreground = Brushes.DimGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand };
            var sBS = new Style(typeof(Border)); sBS.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); bBS.Resources.Add(typeof(Border), sBS);
            bBS.Click += (s, e) => { if (_inputNumberBox.Text.Length > 0) _inputNumberBox.Text = _inputNumberBox.Text.Substring(0, _inputNumberBox.Text.Length - 1); }; Grid.SetRow(bBS, 3); Grid.SetColumn(bBS, 2); kg.Children.Add(bBS);
            p.Children.Add(kg);

            // ★修正: 発信ボタンをフィールド変数 _callBtn に代入
            _callBtn = new Button { Content = GetPhoneIcon(false), Height = 50, Background = _primaryColor, Foreground = Brushes.White, Margin = new Thickness(10, 0, 10, 0) };
            var sC = new Style(typeof(Border)); sC.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(25))); _callBtn.Resources.Add(typeof(Border), sC);
            _callBtn.Click += (s, e) => StartOutgoingCall(); p.Children.Add(_callBtn);
            return p;
        }

        private UIElement CreateIncomingView()
        {
            var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            p.Children.Add(new TextBlock { Text = "着信中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
            p.Children.Add(CreatePulsingIcon(_primaryColor, out _incomingPulse));
            _incomingNameText = new TextBlock { FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(_incomingNameText);
            _incomingNumberText = new TextBlock { FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) }; p.Children.Add(_incomingNumberText);

            var bg = new Grid { Width = 300 }; bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ansBtn = new Button { Content = GetPhoneIcon(false), Height = 60, Background = _acceptColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand };
            var sA = new Style(typeof(Border)); sA.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); ansBtn.Resources.Add(typeof(Border), sA);
            ansBtn.Click += (s, e) => AnswerCall(); Grid.SetColumn(ansBtn, 0); bg.Children.Add(ansBtn);

            var rb = new Button { Content = GetPhoneIcon(true), Height = 60, Background = _dangerColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand };
            var sR = new Style(typeof(Border)); sR.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); rb.Resources.Add(typeof(Border), sR);

            // ★重要修正: 着信拒否 (true)
            rb.Click += (s, e) => EndCall(true);
            Grid.SetColumn(rb, 2); bg.Children.Add(rb);
            p.Children.Add(bg);
            return p;
        }

        private UIElement CreateOutgoingView()
        {
            var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            p.Children.Add(new TextBlock { Text = "呼び出し中...", FontSize = 16, Foreground = _primaryColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
            p.Children.Add(CreatePulsingIcon(_primaryColor, out _outgoingPulse));
            _outgoingNameText = new TextBlock { FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(_outgoingNameText);
            _outgoingNumberText = new TextBlock { FontSize = 24, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50) }; p.Children.Add(_outgoingNumberText);

            var cancelBtn = new Button { Content = GetPhoneIcon(true), Width = 200, Height = 60, Background = _dangerColor, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand };
            var sC = new Style(typeof(Border)); sC.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); cancelBtn.Resources.Add(typeof(Border), sC);
            cancelBtn.Click += (s, e) => EndCall(true); p.Children.Add(cancelBtn);
            return p;
        }

        private UIElement CreateTalkingView()
        {
            var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var ig = CreatePulsingIcon(_acceptColor, out _talkingPulse); _talkingIconBg = (Ellipse)ig.Children[0]; p.Children.Add(ig);
            _talkingStatusText = new TextBlock { Text = "通話中", FontSize = 16, Foreground = _acceptColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 5) }; p.Children.Add(_talkingStatusText);
            _talkingNameText = new TextBlock { FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brushes.Black, Margin = new Thickness(0, 0, 0, 5) }; p.Children.Add(_talkingNameText);
            _talkingTimerText = new TextBlock { FontSize = 20, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center }; p.Children.Add(_talkingTimerText);

            var bg = new Grid { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 3; i++) bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button CreateControlBtn(string icon, string text, RoutedEventHandler handler, out Button btnRef, out TextBlock labelRef)
            {
                var btn = new Button { Background = Brushes.Transparent, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(10), Cursor = System.Windows.Input.Cursors.Hand, Height = 80, Width = 80 };
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var ico = new TextBlock { Text = icon, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray };
                var lbl = new TextBlock { Text = text, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) };
                stack.Children.Add(ico); stack.Children.Add(lbl); btn.Content = stack;
                var style = new Style(typeof(Border)); style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(40))); btn.Resources.Add(typeof(Border), style);
                btn.Click += handler; btnRef = btn; labelRef = lbl; return btn;
            }

            var b1 = CreateControlBtn("🔇", "ミュート", (s, e) => ToggleMute(), out _muteBtn, out _muteBtnLabel); Grid.SetColumn(b1, 0); bg.Children.Add(b1);
            var b2 = CreateControlBtn("🔊", "スピーカー", (s, e) => ToggleSpeaker(), out _speakerBtn, out _muteBtnLabel); Grid.SetColumn(b2, 1); bg.Children.Add(b2);
            var b3 = CreateControlBtn("⏸", "保 留", (s, e) => ToggleHold(), out _holdBtn, out _holdBtnLabel); Grid.SetColumn(b3, 2); bg.Children.Add(b3);
            p.Children.Add(bg);

            var endBtn = new Button { Content = GetPhoneIcon(true), Width = 80, Height = 80, Background = _dangerColor, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20), Cursor = System.Windows.Input.Cursors.Hand };
            var sE = new Style(typeof(Border)); sE.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(40))); endBtn.Resources.Add(typeof(Border), sE);
            endBtn.Click += (s, e) => EndCall(true); p.Children.Add(endBtn);
            return p;
        }

        private Button CreateDialButton(string t)
        {
            var b = new Button { Content = t, FontSize = 24, FontWeight = FontWeights.SemiBold, Background = Brushes.White, Foreground = Brushes.DarkSlateGray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5), Cursor = System.Windows.Input.Cursors.Hand };
            var s = new Style(typeof(Border)); s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30))); b.Resources.Add(typeof(Border), s);
            b.Click += (o, e) => { _inputNumberBox.Text += t; _inputNumberBox.CaretIndex = _inputNumberBox.Text.Length; _inputNumberBox.Focus(); }; return b;
        }
    }
}