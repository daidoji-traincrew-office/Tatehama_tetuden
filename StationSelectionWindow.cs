using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RailwayPhone
{
    // 自局（担当駅）を選択するウィンドウ
    public class StationSelectionWindow : Window
    {
        public PhoneBookEntry SelectedStation { get; private set; }
        private ComboBox _stationCombo;

        // デザイン用カラー
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));

        public StationSelectionWindow(PhoneBookEntry currentStation = null)
        {
            Title = "自局設定";
            Width = 400; Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = _bgColor;

            var root = new StackPanel { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };

            // タイトル
            root.Children.Add(new TextBlock
            {
                Text = "担当部署の選択",
                FontSize = 20,
                FontWeight = FontWeights.Light,
                Foreground = Brushes.DarkSlateGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // 白いカードパネル
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 5 }
            };
            var cardStack = new StackPanel();

            cardStack.Children.Add(new TextBlock { Text = "自局名 (ID):", Margin = new Thickness(0, 0, 0, 5), FontWeight = FontWeights.Bold });

            _stationCombo = new ComboBox { Height = 35, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center };
            _stationCombo.ItemsSource = PhoneBook.Entries;
            _stationCombo.DisplayMemberPath = "Name"; // 名前だけ表示

            // 既に設定済みならそれを選択状態にする
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
            cardStack.Children.Add(new TextBlock { Text = "※この設定でネットワークに参加します。", FontSize = 11, Foreground = Brushes.Gray });

            card.Child = cardStack;
            root.Children.Add(card);

            // OKボタン
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
                IsDefault = true // Enterキーで決定
            };
            // 角丸スタイル
            var style = new Style(typeof(Border)); style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(5)));
            okBtn.Resources.Add(typeof(Border), style);

            okBtn.Click += (s, e) => {
                if (_stationCombo.SelectedItem == null)
                {
                    MessageBox.Show("担当する部署を選択してください。", "未選択", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedStation = _stationCombo.SelectedItem as PhoneBookEntry;
                DialogResult = true; // ウィンドウを閉じて成功を返す
            };

            root.Children.Add(okBtn);
            Content = root;
        }
    }
}