using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RailwayPhone
{
    /// <summary>
    /// アプリケーション起動時または設定変更時に、
    /// 自局（担当する駅・部署）を選択するためのモーダルウィンドウです。
    /// </summary>
    public class StationSelectionWindow : Window
    {
        #region 公開プロパティ

        /// <summary>ユーザーによって選択された駅情報</summary>
        public PhoneBookEntry SelectedStation { get; private set; }

        #endregion

        #region UIコンポーネント

        private ComboBox _stationCombo;

        #endregion

        #region デザイン定数

        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        #endregion

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="currentStation">現在設定されている駅（変更時の初期選択用、省略可）</param>
        public StationSelectionWindow(PhoneBookEntry currentStation = null)
        {
            // ウィンドウの基本設定
            Title = "自局設定";
            Width = 400;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = _bgColor;

            // UIの生成と配置
            InitializeUi(currentStation);
        }

        #region UI構築ロジック

        /// <summary>
        /// 画面内のコントロールを生成・配置します。
        /// </summary>
        private void InitializeUi(PhoneBookEntry currentStation)
        {
            var root = new StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 1. タイトルテキスト
            root.Children.Add(new TextBlock
            {
                Text = "担当部署の選択",
                FontSize = 20,
                FontWeight = FontWeights.Light,
                Foreground = Brushes.DarkSlateGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // 2. 設定エリア（白いカードパネル）
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 2,
                    Opacity = 0.1,
                    BlurRadius = 5
                }
            };

            var cardStack = new StackPanel();

            // ラベル
            cardStack.Children.Add(new TextBlock
            {
                Text = "自局名 (ID):",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.Bold
            });

            // コンボボックス（電話帳リストを表示）
            _stationCombo = new ComboBox
            {
                Height = 35,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new PhoneBookRepository().GetAll(),
                DisplayMemberPath = "Name" // オブジェクトのどのプロパティを表示するか
            };

            // 既に設定がある場合は初期選択
            if (currentStation != null)
            {
                foreach (PhoneBookEntry item in _stationCombo.Items)
                {
                    if (item.Number == currentStation.Number)
                    {
                        _stationCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            cardStack.Children.Add(_stationCombo);

            // 注釈テキスト
            cardStack.Children.Add(new TextBlock
            {
                Text = "※この設定でネットワークに参加します。",
                FontSize = 11,
                Foreground = Brushes.Gray
            });

            card.Child = cardStack;
            root.Children.Add(card);

            // 3. 決定ボタン
            var okBtn = new Button
            {
                Content = "決定して開始",
                Height = 40,
                Margin = new Thickness(0, 20, 0, 0),
                Background = _primaryColor,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = true // Enterキーで決定できるようにする
            };

            // ボタンの角丸スタイル
            var style = new Style(typeof(Border));
            style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(5)));
            okBtn.Resources.Add(typeof(Border), style);

            okBtn.Click += OnOkButtonClick;

            root.Children.Add(okBtn);
            Content = root;
        }

        #endregion

        #region イベントハンドラ

        /// <summary>
        /// 決定ボタンクリック時の処理
        /// </summary>
        private void OnOkButtonClick(object sender, RoutedEventArgs e)
        {
            if (_stationCombo.SelectedItem == null)
            {
                MessageBox.Show("担当する部署を選択してください。", "未選択", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 選択結果を保存してウィンドウを閉じる
            SelectedStation = _stationCombo.SelectedItem as PhoneBookEntry;
            DialogResult = true;
        }

        #endregion
    }
}