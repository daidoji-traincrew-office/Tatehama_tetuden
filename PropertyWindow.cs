using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RailwayPhone
{
    public class PropertyWindow : Window
    {
        public DeviceInfo SelectedInput { get; private set; }
        public DeviceInfo SelectedOutput { get; private set; }
        public float InputVolume { get; private set; } = 1.0f;
        public float OutputVolume { get; private set; } = 1.0f;

        // UIコントロール
        private ComboBox _inputCombo;
        private ComboBox _outputCombo;
        private Slider _inputVolSlider;
        private Slider _outputVolSlider;
        private TextBlock _inputVolText;
        private TextBlock _outputVolText;
        private Button _testBtn;
        private ProgressBar _meter;
        private TextBlock _testStatus;

        // 音声エンジン
        private WasapiCapture _capture;
        private WasapiOut _render;
        private BufferedWaveProvider _buffer;
        private bool _isTesting = false;

        // カラー定義
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        private readonly Brush _cardColor = Brushes.White;

        public PropertyWindow(DeviceInfo currentInput, DeviceInfo currentOutput)
        {
            Title = "オーディオ設定";
            Width = 500; Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = _bgColor;

            // ★修正ポイント1: Closingイベントで確実に止める
            Closing += (s, e) =>
            {
                StopTest();
            };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(20) };
            scroll.Content = root;
            Content = scroll;

            // --- ヘッダー ---
            root.Children.Add(new TextBlock
            {
                Text = "通信デバイス設定",
                FontSize = 20,
                FontWeight = FontWeights.Light,
                Margin = new Thickness(0, 0, 0, 15),
                Foreground = Brushes.DarkSlateGray
            });

            // 1. デバイス選択
            var devicePanel = new StackPanel();
            devicePanel.Children.Add(CreateLabel("マイク (送話):"));
            _inputCombo = CreateStyledComboBox();
            _inputCombo.SelectionChanged += (s, e) => StopTest();
            devicePanel.Children.Add(_inputCombo);

            devicePanel.Children.Add(CreateLabel("スピーカー (受話):"));
            _outputCombo = CreateStyledComboBox();
            _outputCombo.SelectionChanged += (s, e) => StopTest();
            devicePanel.Children.Add(_outputCombo);

            root.Children.Add(CreateCard("デバイス選択", devicePanel));

            // 2. 音量設定
            var volGrid = new Grid();
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 入力音量
            var inVolPanel = new StackPanel();
            var inHeader = new DockPanel();
            inHeader.Children.Add(CreateLabel("マイク感度"));
            _inputVolText = new TextBlock { Text = "100%", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            inHeader.Children.Add(_inputVolText);
            inVolPanel.Children.Add(inHeader);

            _inputVolSlider = new Slider { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 50, IsSnapToTickEnabled = false };
            _inputVolSlider.ValueChanged += (s, e) => {
                _inputVolText.Text = $"{(int)_inputVolSlider.Value}%";
                InputVolume = (float)(_inputVolSlider.Value / 100.0);
            };
            inVolPanel.Children.Add(_inputVolSlider);
            Grid.SetColumn(inVolPanel, 0);

            // 出力音量
            var outVolPanel = new StackPanel();
            var outHeader = new DockPanel();
            outHeader.Children.Add(CreateLabel("受話音量"));
            _outputVolText = new TextBlock { Text = "100%", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            outHeader.Children.Add(_outputVolText);
            outVolPanel.Children.Add(outHeader);

            _outputVolSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 10 };
            _outputVolSlider.ValueChanged += (s, e) => {
                _outputVolText.Text = $"{(int)_outputVolSlider.Value}%";
                OutputVolume = (float)(_outputVolSlider.Value / 100.0);
            };
            outVolPanel.Children.Add(_outputVolSlider);
            Grid.SetColumn(outVolPanel, 2);

            volGrid.Children.Add(inVolPanel);
            volGrid.Children.Add(outVolPanel);

            root.Children.Add(CreateCard("音量調整", volGrid));

            // 3. テストエリア
            var testPanel = new StackPanel();
            testPanel.Children.Add(new TextBlock
            {
                Text = "マイクに向かって話し、スピーカーから自分の声が聞こえるか確認します。",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var testCtrls = new Grid();
            testCtrls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            testCtrls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            testCtrls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            _meter = new ProgressBar { Height = 30, Maximum = 100, Foreground = _primaryColor, Background = Brushes.WhiteSmoke, BorderThickness = new Thickness(0) };
            _meter.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 280, 30), RadiusX = 4, RadiusY = 4 };

            Grid.SetColumn(_meter, 0);
            testCtrls.Children.Add(_meter);

            _testBtn = new Button
            {
                Content = "テスト開始",
                Height = 30,
                Background = _primaryColor,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            var btnStyle = new Style(typeof(Border));
            btnStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4)));
            _testBtn.Resources.Add(typeof(Border), btnStyle);

            _testBtn.Click += (s, e) => ToggleTest();
            Grid.SetColumn(_testBtn, 2);
            testCtrls.Children.Add(_testBtn);

            testPanel.Children.Add(testCtrls);
            _testStatus = new TextBlock { Text = "待機中", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0), FontSize = 11 };
            testPanel.Children.Add(_testStatus);

            root.Children.Add(CreateCard("動作テスト", testPanel));

            // 4. フッター
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };

            var okBtn = new Button
            {
                Content = "設定を保存",
                Width = 120,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true,
                Background = _primaryColor,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            okBtn.Click += (s, e) => {
                StopTest();
                SelectedInput = _inputCombo.SelectedItem as DeviceInfo;
                SelectedOutput = _outputCombo.SelectedItem as DeviceInfo;
                DialogResult = true;
            };

            var cancelBtn = new Button
            {
                Content = "キャンセル",
                Width = 100,
                Height = 35,
                IsCancel = true,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            cancelBtn.Click += (s, e) => {
                StopTest();
                DialogResult = false;
            };

            footer.Children.Add(okBtn);
            footer.Children.Add(cancelBtn);
            root.Children.Add(footer);

            LoadDevices(currentInput, currentOutput);
        }

        private Border CreateCard(string headerText, UIElement content)
        {
            var border = new Border
            {
                Background = _cardColor,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 15),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 5 }
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = headerText, FontWeight = FontWeights.Bold, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 10) });
            stack.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.WhiteSmoke });
            stack.Children.Add(content);
            border.Child = stack;
            return border;
        }

        private TextBlock CreateLabel(string text) => new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 5), FontSize = 12, Foreground = Brushes.Black };
        private ComboBox CreateStyledComboBox() => new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 15), Padding = new Thickness(5) };

        private void ToggleTest()
        {
            if (_isTesting) StopTest();
            else StartTest();
        }

        private void StartTest()
        {
            var inInfo = _inputCombo.SelectedItem as DeviceInfo;
            var outInfo = _outputCombo.SelectedItem as DeviceInfo;
            if (inInfo == null || outInfo == null) return;

            try
            {
                var enumerator = new MMDeviceEnumerator();
                var inDevice = enumerator.GetDevice(inInfo.ID);
                var outDevice = enumerator.GetDevice(outInfo.ID);

                var format = new WaveFormat(44100, 16, 1);
                _buffer = new BufferedWaveProvider(format);
                _buffer.DiscardOnBufferOverflow = true;

                _capture = new WasapiCapture(inDevice);
                _capture.WaveFormat = format;

                _capture.DataAvailable += (s, e) =>
                {
                    // ★修正ポイント2: 終了処理中なら何もしない
                    if (!_isTesting) return;

                    float currentInVol = InputVolume;
                    float currentOutVol = OutputVolume;
                    float maxSampleInFrame = 0;

                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        short sample = BitConverter.ToInt16(e.Buffer, i);
                        int processedIn = (int)(sample * currentInVol);

                        if (processedIn > 32767) processedIn = 32767;
                        if (processedIn < -32768) processedIn = -32768;

                        float val = Math.Abs(processedIn) / 32768f;
                        if (val > maxSampleInFrame) maxSampleInFrame = val;

                        int processedOut = (int)(processedIn * currentOutVol);
                        if (processedOut > 32767) processedOut = 32767;
                        if (processedOut < -32768) processedOut = -32768;

                        byte[] bytes = BitConverter.GetBytes((short)processedOut);
                        e.Buffer[i] = bytes[0];
                        e.Buffer[i + 1] = bytes[1];
                    }

                    if (_buffer != null) _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

                    // ★修正ポイント3: UIが生きてるかチェックしてからInvokeする
                    try
                    {
                        Dispatcher.Invoke(() => {
                            if (this.IsLoaded && _meter != null)
                            {
                                _meter.Value = maxSampleInFrame * 100;
                            }
                        });
                    }
                    catch
                    {
                        // ウィンドウが閉じている間のエラーは無視する
                    }
                };

                _render = new WasapiOut(outDevice, AudioClientShareMode.Shared, true, 200);
                _render.Init(_buffer);

                _capture.StartRecording();
                _render.Play();

                _isTesting = true;
                _testBtn.Content = "テスト停止";
                _testBtn.Background = _dangerColor;
                _testStatus.Text = "● テスト中";
                _testStatus.Foreground = _primaryColor;
            }
            catch (Exception ex)
            {
                StopTest();
                MessageBox.Show($"エラー: {ex.Message}");
            }
        }

        private void StopTest()
        {
            // まずフラグを折って、DataAvailableでの処理を止める
            _isTesting = false;

            try
            {
                // UI更新
                if (this.IsLoaded)
                {
                    _testBtn.Content = "テスト開始";
                    _testBtn.Background = _primaryColor;
                    _testStatus.Text = "待機中";
                    _testStatus.Foreground = Brushes.Gray;
                    _meter.Value = 0;
                }

                // リソース解放
                if (_capture != null)
                {
                    _capture.StopRecording();
                    _capture.Dispose();
                    _capture = null;
                }

                if (_render != null)
                {
                    _render.Stop();
                    _render.Dispose();
                    _render = null;
                }

                _buffer = null;
            }
            catch
            {
                // 終了時のエラーは無視してよい
            }
        }

        private void LoadDevices(DeviceInfo currentInput, DeviceInfo currentOutput)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                _inputCombo.Items.Clear();
                foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    var info = new DeviceInfo { Name = dev.FriendlyName, ID = dev.ID };
                    _inputCombo.Items.Add(info);
                    if (currentInput != null && info.ID == currentInput.ID) _inputCombo.SelectedItem = info;
                }
                if (_inputCombo.SelectedIndex < 0 && _inputCombo.Items.Count > 0) _inputCombo.SelectedIndex = 0;

                _outputCombo.Items.Clear();
                foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var info = new DeviceInfo { Name = dev.FriendlyName, ID = dev.ID };
                    _outputCombo.Items.Add(info);
                    if (currentOutput != null && info.ID == currentOutput.ID) _outputCombo.SelectedItem = info;
                }
                if (_outputCombo.SelectedIndex < 0 && _outputCombo.Items.Count > 0) _outputCombo.SelectedIndex = 0;
            }
            catch { }
        }
    }
}