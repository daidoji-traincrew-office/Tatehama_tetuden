using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RailwayPhone
{
    // 音声デバイス専用の設定ウィンドウ
    public class AudioSettingWindow : Window
    {
        public DeviceInfo SelectedInput { get; private set; }
        public DeviceInfo SelectedOutput { get; private set; }
        public float InputVolume { get; private set; }
        public float OutputVolume { get; private set; }

        // UI
        private ComboBox _inputCombo;
        private ComboBox _outputCombo;
        private Slider _inputVolSlider;
        private Slider _outputVolSlider;
        private TextBlock _inputVolText;
        private TextBlock _outputVolText;
        private Button _testBtn;
        private ProgressBar _meter;
        private TextBlock _testStatus;

        // Audio Engine
        private WasapiCapture _capture;
        private WasapiOut _render;
        private BufferedWaveProvider _buffer;
        private bool _isTesting = false;

        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        public AudioSettingWindow(DeviceInfo currentInput, DeviceInfo currentOutput, float inVol, float outVol)
        {
            Title = "音声設定";
            Width = 500; Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = _bgColor;

            // 初期値を保存
            InputVolume = inVol; OutputVolume = outVol;

            Closing += (s, e) => StopTest();

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(20) };
            scroll.Content = root;
            Content = scroll;

            root.Children.Add(new TextBlock { Text = "オーディオデバイス設定", FontSize = 20, FontWeight = FontWeights.Light, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 0, 0, 20) });

            // 1. デバイス選択
            var devicePanel = new StackPanel();
            devicePanel.Children.Add(CreateLabel("マイク (送話):"));
            _inputCombo = CreateComboBox(); _inputCombo.SelectionChanged += (s, e) => StopTest();
            devicePanel.Children.Add(_inputCombo);

            devicePanel.Children.Add(CreateLabel("スピーカー (受話):"));
            _outputCombo = CreateComboBox(); _outputCombo.SelectionChanged += (s, e) => StopTest();
            devicePanel.Children.Add(_outputCombo);
            root.Children.Add(CreateCard("デバイス選択", devicePanel));

            // 2. 音量設定
            var volGrid = new Grid();
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Input Vol
            var inPanel = new StackPanel();
            var inHead = new DockPanel(); inHead.Children.Add(CreateLabel("マイク感度"));
            _inputVolText = new TextBlock { Text = $"{(int)(InputVolume * 100)}%", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            inHead.Children.Add(_inputVolText); inPanel.Children.Add(inHead);
            _inputVolSlider = new Slider { Minimum = 0, Maximum = 200, Value = InputVolume * 100, TickFrequency = 10, IsSnapToTickEnabled = false };
            _inputVolSlider.ValueChanged += (s, e) => { _inputVolText.Text = $"{(int)_inputVolSlider.Value}%"; InputVolume = (float)(_inputVolSlider.Value / 100.0); };
            inPanel.Children.Add(_inputVolSlider); Grid.SetColumn(inPanel, 0);

            // Output Vol
            var outPanel = new StackPanel();
            var outHead = new DockPanel(); outHead.Children.Add(CreateLabel("受話音量"));
            _outputVolText = new TextBlock { Text = $"{(int)(OutputVolume * 100)}%", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            outHead.Children.Add(_outputVolText); outPanel.Children.Add(outHead);
            _outputVolSlider = new Slider { Minimum = 0, Maximum = 100, Value = OutputVolume * 100, TickFrequency = 10 };
            _outputVolSlider.ValueChanged += (s, e) => { _outputVolText.Text = $"{(int)_outputVolSlider.Value}%"; OutputVolume = (float)(_outputVolSlider.Value / 100.0); };
            outPanel.Children.Add(_outputVolSlider); Grid.SetColumn(outPanel, 2);

            volGrid.Children.Add(inPanel); volGrid.Children.Add(outPanel);
            root.Children.Add(CreateCard("音量調整", volGrid));

            // 3. テスト
            var testPanel = new StackPanel();
            testPanel.Children.Add(new TextBlock { Text = "マイクに向かって話し、スピーカーから自分の声が返ってくるか確認します。", FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) });
            var tGrid = new Grid(); tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            _meter = new ProgressBar { Height = 30, Maximum = 100, Foreground = _primaryColor, Background = Brushes.WhiteSmoke, BorderThickness = new Thickness(0) };
            _meter.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 280, 30), RadiusX = 4, RadiusY = 4 };
            Grid.SetColumn(_meter, 0); tGrid.Children.Add(_meter);
            _testBtn = new Button { Content = "テスト開始", Height = 30, Background = _primaryColor, Foreground = Brushes.White, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            var bStyle = new Style(typeof(Border)); bStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4))); _testBtn.Resources.Add(typeof(Border), bStyle);
            _testBtn.Click += (s, e) => ToggleTest(); Grid.SetColumn(_testBtn, 2); tGrid.Children.Add(_testBtn);
            testPanel.Children.Add(tGrid);
            _testStatus = new TextBlock { Text = "待機中", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0), FontSize = 11 };
            testPanel.Children.Add(_testStatus);
            root.Children.Add(CreateCard("動作テスト", testPanel));

            // Footer
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "設定を保存", Width = 120, Height = 35, Margin = new Thickness(0, 0, 10, 0), IsDefault = true, Background = _primaryColor, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            ok.Click += (s, e) => { StopTest(); SelectedInput = _inputCombo.SelectedItem as DeviceInfo; SelectedOutput = _outputCombo.SelectedItem as DeviceInfo; DialogResult = true; };
            var cancel = new Button { Content = "キャンセル", Width = 100, Height = 35, IsCancel = true, Background = Brushes.Transparent, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            cancel.Click += (s, e) => { StopTest(); DialogResult = false; };
            footer.Children.Add(ok); footer.Children.Add(cancel);
            root.Children.Add(footer);

            LoadDevices(currentInput, currentOutput);
        }

        // Helpers
        private Border CreateCard(string head, UIElement c)
        {
            var b = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 15), Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 5 } };
            var s = new StackPanel(); s.Children.Add(new TextBlock { Text = head, FontWeight = FontWeights.Bold, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 10) }); s.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.WhiteSmoke }); s.Children.Add(c); b.Child = s; return b;
        }
        private TextBlock CreateLabel(string t) => new TextBlock { Text = t, Margin = new Thickness(0, 0, 0, 5), FontSize = 12 };
        private ComboBox CreateComboBox() => new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 15), Padding = new Thickness(5) };

        // Logic
        private void ToggleTest() { if (_isTesting) StopTest(); else StartTest(); }
        private void StartTest()
        {
            var iInfo = _inputCombo.SelectedItem as DeviceInfo; var oInfo = _outputCombo.SelectedItem as DeviceInfo;
            if (iInfo == null || oInfo == null) return;
            try
            {
                var mm = new MMDeviceEnumerator();
                var fmt = new WaveFormat(44100, 16, 1);
                _buffer = new BufferedWaveProvider(fmt) { DiscardOnBufferOverflow = true };
                _capture = new WasapiCapture(mm.GetDevice(iInfo.ID)) { WaveFormat = fmt };
                _capture.DataAvailable += (s, e) => {
                    if (!_isTesting) return;
                    float max = 0;
                    for (int k = 0; k < e.BytesRecorded; k += 2)
                    {
                        short smp = BitConverter.ToInt16(e.Buffer, k);
                        int pIn = (int)(smp * InputVolume); if (pIn > 32767) pIn = 32767; if (pIn < -32768) pIn = -32768;
                        float v = Math.Abs(pIn) / 32768f; if (v > max) max = v;
                        int pOut = (int)(pIn * OutputVolume); if (pOut > 32767) pOut = 32767; if (pOut < -32768) pOut = -32768;
                        byte[] b = BitConverter.GetBytes((short)pOut); e.Buffer[k] = b[0]; e.Buffer[k + 1] = b[1];
                    }
                    if (_buffer != null) _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    try { Dispatcher.Invoke(() => { if (IsLoaded && _meter != null) _meter.Value = max * 100; }); } catch { }
                };
                _render = new WasapiOut(mm.GetDevice(oInfo.ID), AudioClientShareMode.Shared, true, 200); _render.Init(_buffer);
                _capture.StartRecording(); _render.Play();
                _isTesting = true; _testBtn.Content = "テスト停止"; _testBtn.Background = _dangerColor; _testStatus.Text = "● テスト中"; _testStatus.Foreground = _primaryColor;
            }
            catch (Exception ex) { StopTest(); MessageBox.Show($"エラー: {ex.Message}"); }
        }
        private void StopTest()
        {
            _isTesting = false;
            try
            {
                if (IsLoaded) { _testBtn.Content = "テスト開始"; _testBtn.Background = _primaryColor; _testStatus.Text = "待機中"; _testStatus.Foreground = Brushes.Gray; _meter.Value = 0; }
                _capture?.StopRecording(); _capture?.Dispose(); _capture = null;
                _render?.Stop(); _render?.Dispose(); _render = null; _buffer = null;
            }
            catch { }
        }
        private void LoadDevices(DeviceInfo cI, DeviceInfo cO)
        {
            try
            {
                var mm = new MMDeviceEnumerator();
                _inputCombo.Items.Clear();
                foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    var i = new DeviceInfo { Name = d.FriendlyName, ID = d.ID }; _inputCombo.Items.Add(i);
                    if (cI != null && i.ID == cI.ID) _inputCombo.SelectedItem = i;
                }
                if (_inputCombo.SelectedIndex < 0 && _inputCombo.Items.Count > 0) _inputCombo.SelectedIndex = 0;
                _outputCombo.Items.Clear();
                foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var i = new DeviceInfo { Name = d.FriendlyName, ID = d.ID }; _outputCombo.Items.Add(i);
                    if (cO != null && i.ID == cO.ID) _outputCombo.SelectedItem = i;
                }
                if (_outputCombo.SelectedIndex < 0 && _outputCombo.Items.Count > 0) _outputCombo.SelectedIndex = 0;
            }
            catch { }
        }
    }
}