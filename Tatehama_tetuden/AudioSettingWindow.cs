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
    /// <summary>
    /// 音声デバイス（マイク・スピーカー）の選択と音量調整、
    /// およびループバックテストを行うための設定ウィンドウクラス。
    /// </summary>
    public class AudioSettingWindow : Window
    {
        #region 公開プロパティ (設定結果)

        /// <summary>選択された入力デバイス（マイク）</summary>
        public DeviceInfo SelectedInput { get; private set; }

        /// <summary>選択された出力デバイス（スピーカー）</summary>
        public DeviceInfo SelectedOutput { get; private set; }

        /// <summary>設定された入力音量倍率 (0.0 ~ 2.0)</summary>
        public float InputVolume { get; private set; }

        /// <summary>設定された出力音量倍率 (0.0 ~ 1.0)</summary>
        public float OutputVolume { get; private set; }

        #endregion

        #region UIコンポーネント

        private ComboBox _inputCombo;
        private ComboBox _outputCombo;
        private Slider _inputVolSlider;
        private Slider _outputVolSlider;
        private TextBlock _inputVolText;
        private TextBlock _outputVolText;
        private Button _testBtn;
        private ProgressBar _meter;
        private TextBlock _testStatus;

        #endregion

        #region オーディオエンジン変数

        // マイク入力用 (WASAPI)
        private WasapiCapture _capture;
        // スピーカー出力用 (WASAPI)
        private WasapiOut _render;
        // リングバッファ (入力と出力を繋ぐ)
        private BufferedWaveProvider _buffer;
        // テスト実行中フラグ
        private bool _isTesting = false;

        #endregion

        #region デザイン定数

        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _dangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        #endregion

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="currentInput">現在の入力デバイス</param>
        /// <param name="currentOutput">現在の出力デバイス</param>
        /// <param name="inVol">現在の入力音量</param>
        /// <param name="outVol">現在の出力音量</param>
        public AudioSettingWindow(DeviceInfo currentInput, DeviceInfo currentOutput, float inVol, float outVol)
        {
            // --- ウィンドウの基本設定 ---
            Title = "音声設定";
            Width = 500;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = _bgColor;

            // 初期値を保存
            InputVolume = inVol;
            OutputVolume = outVol;

            // ウィンドウが閉じられる際にテストを停止する
            Closing += (s, e) => StopTest();

            // --- UIの構築 (コードビハインドで生成) ---
            InitializeUi();

            // --- デバイス情報の読み込み ---
            LoadDevices(currentInput, currentOutput);
        }

        #region UI構築ロジック

        /// <summary>
        /// 画面レイアウトとコントロールを生成します。
        /// </summary>
        private void InitializeUi()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(20) };
            scroll.Content = root;
            Content = scroll;

            // タイトル
            root.Children.Add(new TextBlock
            {
                Text = "オーディオデバイス設定",
                FontSize = 20,
                FontWeight = FontWeights.Light,
                Foreground = Brushes.DarkSlateGray,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // 1. デバイス選択セクション
            var devicePanel = new StackPanel();

            // マイク選択
            devicePanel.Children.Add(CreateLabel("マイク (送話):"));
            _inputCombo = CreateComboBox();
            _inputCombo.SelectionChanged += (s, e) => StopTest(); // 変更時はテスト停止
            devicePanel.Children.Add(_inputCombo);

            // スピーカー選択
            devicePanel.Children.Add(CreateLabel("スピーカー (受話):"));
            _outputCombo = CreateComboBox();
            _outputCombo.SelectionChanged += (s, e) => StopTest();
            devicePanel.Children.Add(_outputCombo);

            root.Children.Add(CreateCard("デバイス選択", devicePanel));

            // 2. 音量設定セクション
            var volGrid = new Grid();
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // マイク音量 (Input Volume)
            var inPanel = new StackPanel();
            var inHead = new DockPanel();
            inHead.Children.Add(CreateLabel("マイク感度"));
            _inputVolText = new TextBlock
            {
                Text = $"{(int)(InputVolume * 100)}%",
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            inHead.Children.Add(_inputVolText);
            inPanel.Children.Add(inHead);

            _inputVolSlider = new Slider
            {
                Minimum = 0,
                Maximum = 200, // 200%まで増幅可能
                Value = InputVolume * 100,
                TickFrequency = 10,
                IsSnapToTickEnabled = false
            };
            _inputVolSlider.ValueChanged += (s, e) =>
            {
                _inputVolText.Text = $"{(int)_inputVolSlider.Value}%";
                InputVolume = (float)(_inputVolSlider.Value / 100.0);
            };
            inPanel.Children.Add(_inputVolSlider);
            Grid.SetColumn(inPanel, 0);

            // スピーカー音量 (Output Volume)
            var outPanel = new StackPanel();
            var outHead = new DockPanel();
            outHead.Children.Add(CreateLabel("受話音量"));
            _outputVolText = new TextBlock
            {
                Text = $"{(int)(OutputVolume * 100)}%",
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            outHead.Children.Add(_outputVolText);
            outPanel.Children.Add(outHead);

            _outputVolSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = OutputVolume * 100,
                TickFrequency = 10
            };
            _outputVolSlider.ValueChanged += (s, e) =>
            {
                _outputVolText.Text = $"{(int)_outputVolSlider.Value}%";
                OutputVolume = (float)(_outputVolSlider.Value / 100.0);
            };
            outPanel.Children.Add(_outputVolSlider);
            Grid.SetColumn(outPanel, 2);

            volGrid.Children.Add(inPanel);
            volGrid.Children.Add(outPanel);
            root.Children.Add(CreateCard("音量調整", volGrid));

            // 3. 動作テストセクション
            var testPanel = new StackPanel();
            testPanel.Children.Add(new TextBlock
            {
                Text = "マイクに向かって話し、スピーカーから自分の声が返ってくるか確認します。",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var tGrid = new Grid();
            tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // 音量メーター
            _meter = new ProgressBar
            {
                Height = 30,
                Maximum = 100,
                Foreground = _primaryColor,
                Background = Brushes.WhiteSmoke,
                BorderThickness = new Thickness(0)
            };
            _meter.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 280, 30), RadiusX = 4, RadiusY = 4 };
            Grid.SetColumn(_meter, 0);
            tGrid.Children.Add(_meter);

            // テスト開始/停止ボタン
            _testBtn = new Button
            {
                Content = "テスト開始",
                Height = 30,
                Background = _primaryColor,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            var bStyle = new Style(typeof(Border));
            bStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4)));
            _testBtn.Resources.Add(typeof(Border), bStyle);
            _testBtn.Click += (s, e) => ToggleTest();
            Grid.SetColumn(_testBtn, 2);
            tGrid.Children.Add(_testBtn);

            testPanel.Children.Add(tGrid);

            _testStatus = new TextBlock
            {
                Text = "待機中",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 11
            };
            testPanel.Children.Add(_testStatus);
            root.Children.Add(CreateCard("動作テスト", testPanel));

            // 4. フッター (保存・キャンセル)
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var ok = new Button
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
            ok.Click += (s, e) =>
            {
                StopTest();
                SelectedInput = _inputCombo.SelectedItem as DeviceInfo;
                SelectedOutput = _outputCombo.SelectedItem as DeviceInfo;
                DialogResult = true; // 成功として閉じる
            };

            var cancel = new Button
            {
                Content = "キャンセル",
                Width = 100,
                Height = 35,
                IsCancel = true,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            cancel.Click += (s, e) =>
            {
                StopTest();
                DialogResult = false; // キャンセルとして閉じる
            };

            footer.Children.Add(ok);
            footer.Children.Add(cancel);
            root.Children.Add(footer);
        }

        // --- UI生成ヘルパーメソッド ---

        private Border CreateCard(string head, UIElement c)
        {
            var b = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 15),
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 5 }
            };
            var s = new StackPanel();
            s.Children.Add(new TextBlock
            {
                Text = head,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 10)
            });
            s.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.WhiteSmoke });
            s.Children.Add(c);
            b.Child = s;
            return b;
        }

        private TextBlock CreateLabel(string t) => new TextBlock
        {
            Text = t,
            Margin = new Thickness(0, 0, 0, 5),
            FontSize = 12
        };

        private ComboBox CreateComboBox() => new ComboBox
        {
            Height = 30,
            Margin = new Thickness(0, 0, 0, 15),
            Padding = new Thickness(5)
        };

        #endregion

        #region オーディオ処理ロジック (NAudio)

        /// <summary>
        /// 利用可能なオーディオデバイスを読み込み、コンボボックスに設定します。
        /// </summary>
        private void LoadDevices(DeviceInfo currentInput, DeviceInfo currentOutput)
        {
            try
            {
                var mm = new MMDeviceEnumerator();

                // 入力デバイスの列挙
                _inputCombo.Items.Clear();
                foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    var item = new DeviceInfo { Name = d.FriendlyName, ID = d.ID };
                    _inputCombo.Items.Add(item);
                    // 現在の設定とIDが一致すれば選択状態にする
                    if (currentInput != null && item.ID == currentInput.ID)
                        _inputCombo.SelectedItem = item;
                }
                // 未選択なら先頭を選択
                if (_inputCombo.SelectedIndex < 0 && _inputCombo.Items.Count > 0)
                    _inputCombo.SelectedIndex = 0;

                // 出力デバイスの列挙
                _outputCombo.Items.Clear();
                foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var item = new DeviceInfo { Name = d.FriendlyName, ID = d.ID };
                    _outputCombo.Items.Add(item);
                    if (currentOutput != null && item.ID == currentOutput.ID)
                        _outputCombo.SelectedItem = item;
                }
                if (_outputCombo.SelectedIndex < 0 && _outputCombo.Items.Count > 0)
                    _outputCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"デバイスの読み込みに失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ループバックテストの開始/停止を切り替えます。
        /// </summary>
        private void ToggleTest()
        {
            if (_isTesting) StopTest();
            else StartTest();
        }

        /// <summary>
        /// マイク入力をスピーカーに出力するループバックテストを開始します。
        /// </summary>
        private void StartTest()
        {
            var iInfo = _inputCombo.SelectedItem as DeviceInfo;
            var oInfo = _outputCombo.SelectedItem as DeviceInfo;

            if (iInfo == null || oInfo == null)
            {
                MessageBox.Show("デバイスが選択されていません。");
                return;
            }

            try
            {
                var mm = new MMDeviceEnumerator();

                // サンプリングレートの設定 (44.1kHz, 16bit, Mono)
                var fmt = new WaveFormat(44100, 16, 1);

                // 入力と出力を繋ぐバッファ (オーバーフロー時は古いデータを捨てる)
                _buffer = new BufferedWaveProvider(fmt) { DiscardOnBufferOverflow = true };

                // --- マイク入力 (Capture) 設定 ---
                _capture = new WasapiCapture(mm.GetDevice(iInfo.ID)) { WaveFormat = fmt };

                _capture.DataAvailable += (s, e) =>
                {
                    if (!_isTesting) return;

                    // 音量メーター用の最大値計算と、入力ボリュームの適用
                    float maxVol = 0;
                    for (int k = 0; k < e.BytesRecorded; k += 2)
                    {
                        // 16bit PCMなので2バイトずつ読み込み
                        short sample = BitConverter.ToInt16(e.Buffer, k);

                        // 1. 入力音量の適用 (増幅)
                        int processedIn = (int)(sample * InputVolume);
                        // クリッピング処理 (16bitの範囲内に収める)
                        if (processedIn > 32767) processedIn = 32767;
                        if (processedIn < -32768) processedIn = -32768;

                        // メーター用 (絶対値の最大を記録)
                        float val = Math.Abs(processedIn) / 32768f;
                        if (val > maxVol) maxVol = val;

                        // 2. 出力音量の適用 (減衰/増幅) -> 実際に聞こえる音
                        int processedOut = (int)(processedIn * OutputVolume);
                        if (processedOut > 32767) processedOut = 32767;
                        if (processedOut < -32768) processedOut = -32768;

                        // バッファに書き戻す
                        byte[] b = BitConverter.GetBytes((short)processedOut);
                        e.Buffer[k] = b[0];
                        e.Buffer[k + 1] = b[1];
                    }

                    // 処理後の音声データをバッファに追加 (これでスピーカーから流れる)
                    if (_buffer != null)
                        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

                    // UIスレッドでメーター更新
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (IsLoaded && _meter != null) _meter.Value = maxVol * 100;
                        });
                    }
                    catch { }
                };

                // --- スピーカー出力 (Render) 設定 ---
                // Sharedモード, レイテンシ200ms
                _render = new WasapiOut(mm.GetDevice(oInfo.ID), AudioClientShareMode.Shared, true, 200);
                _render.Init(_buffer);

                // 開始
                _capture.StartRecording();
                _render.Play();

                // UI更新
                _isTesting = true;
                _testBtn.Content = "テスト停止";
                _testBtn.Background = _dangerColor;
                _testStatus.Text = "● テスト中 (ハウリングに注意)";
                _testStatus.Foreground = _primaryColor;
            }
            catch (Exception ex)
            {
                StopTest();
                MessageBox.Show($"オーディオエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ループバックテストを停止し、リソースを解放します。
        /// </summary>
        private void StopTest()
        {
            _isTesting = false;
            try
            {
                // UIリセット
                if (IsLoaded)
                {
                    _testBtn.Content = "テスト開始";
                    _testBtn.Background = _primaryColor;
                    _testStatus.Text = "待機中";
                    _testStatus.Foreground = Brushes.Gray;
                    _meter.Value = 0;
                }

                // リソース解放
                _capture?.StopRecording();
                _capture?.Dispose();
                _capture = null;

                _render?.Stop();
                _render?.Dispose();
                _render = null;

                _buffer = null;
            }
            catch { }
        }

        #endregion
    }
}