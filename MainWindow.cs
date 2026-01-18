using System;
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
        private TextBlock _statusText;
        private TextBlock _selectedNumberText; // 選択中の番号を大きく表示用
        private ListView _phoneBookList;

        // デザイン用カラー
        private readonly Brush _primaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private readonly Brush _bgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248)); // 背景色を少しリッチに

        public MainWindow()
        {
            Title = "模擬鉄 指令電話端末";
            Width = 900; Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _bgColor;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var dockPanel = new DockPanel();

            // --- 1. メニューバー (白背景でモダンに) ---
            var menu = new Menu { Background = Brushes.White, Padding = new Thickness(5) };
            menu.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 };
            DockPanel.SetDock(menu, Dock.Top);

            var settingsItem = new MenuItem { Header = "設定(_S)" };
            var propItem = new MenuItem { Header = "プロパティ(_P)..." };
            propItem.Click += OpenPropertySettings;
            settingsItem.Items.Add(propItem);

            var exitItem = new MenuItem { Header = "終了(_X)" };
            exitItem.Click += (s, e) => Close();
            settingsItem.Items.Add(new Separator());
            settingsItem.Items.Add(exitItem);

            menu.Items.Add(settingsItem);
            dockPanel.Children.Add(menu);

            // --- 2. メインレイアウト ---
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); // 左カラム少し広めに
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // === 左側: 電話帳リスト ===
            var leftPanel = new DockPanel { Margin = new Thickness(15, 15, 5, 15) };

            // ヘッダー
            var listHeader = new TextBlock
            {
                Text = "連絡先リスト",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray,
                Margin = new Thickness(5, 0, 0, 10)
            };
            DockPanel.SetDock(listHeader, Dock.Top);

            // リストビュー本体
            _phoneBookList = new ListView
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch // アイテムを横幅いっぱいに
            };

            // ★ここがポイント: データテンプレート（カードのデザイン定義）
            // C#コードでXAMLの <DataTemplate> を書くのは大変なので、FrameworkElementFactoryを使います
            var itemTemplate = new DataTemplate();
            var factoryBorder = new FrameworkElementFactory(typeof(Border));
            factoryBorder.SetValue(Border.BackgroundProperty, Brushes.White);
            factoryBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            factoryBorder.SetValue(Border.PaddingProperty, new Thickness(12));
            factoryBorder.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8)); // アイテム間の隙間

            // 影をつける
            var dropShadow = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, Opacity = 0.1, BlurRadius = 4 };
            factoryBorder.SetValue(Border.EffectProperty, dropShadow);

            // 中身のレイアウト (Grid)
            var factoryGrid = new FrameworkElementFactory(typeof(Grid));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition)); col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(50)); // アイコン
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition)); col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star)); // 名前
            var col3 = new FrameworkElementFactory(typeof(ColumnDefinition)); col3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto); // 番号
            factoryGrid.AppendChild(col1);
            factoryGrid.AppendChild(col2);
            factoryGrid.AppendChild(col3);

            // 1. アイコン (円)
            var factoryEllipse = new FrameworkElementFactory(typeof(Ellipse));
            factoryEllipse.SetValue(Ellipse.WidthProperty, 36.0);
            factoryEllipse.SetValue(Ellipse.HeightProperty, 36.0);
            factoryEllipse.SetValue(Ellipse.FillProperty, Brushes.WhiteSmoke);
            factoryEllipse.SetValue(Grid.ColumnProperty, 0);
            factoryGrid.AppendChild(factoryEllipse);

            // アイコンの中の文字 (カテゴリの頭文字などを入れたいところだが、今は電話アイコンっぽく "☎")
            var factoryIconText = new FrameworkElementFactory(typeof(TextBlock));
            factoryIconText.SetValue(TextBlock.TextProperty, "📞");
            factoryIconText.SetValue(TextBlock.FontSizeProperty, 16.0);
            factoryIconText.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factoryIconText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            factoryIconText.SetValue(Grid.ColumnProperty, 0);
            factoryGrid.AppendChild(factoryIconText);

            // 2. 名前
            var factoryName = new FrameworkElementFactory(typeof(TextBlock));
            factoryName.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            factoryName.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            factoryName.SetValue(TextBlock.FontSizeProperty, 14.0);
            factoryName.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            factoryName.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            factoryName.SetValue(Grid.ColumnProperty, 1);
            factoryGrid.AppendChild(factoryName);

            // 3. 番号 (右寄せ)
            var factoryNumber = new FrameworkElementFactory(typeof(TextBlock));
            factoryNumber.SetBinding(TextBlock.TextProperty, new Binding("Number"));
            factoryNumber.SetValue(TextBlock.FontSizeProperty, 18.0);
            factoryNumber.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas")); // 等幅フォントでデジタルっぽく
            factoryNumber.SetValue(TextBlock.ForegroundProperty, _primaryColor);
            factoryNumber.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            factoryNumber.SetValue(Grid.ColumnProperty, 2);
            factoryGrid.AppendChild(factoryNumber);

            factoryBorder.AppendChild(factoryGrid);
            itemTemplate.VisualTree = factoryBorder;
            _phoneBookList.ItemTemplate = itemTemplate;

            // データセット
            _phoneBookList.ItemsSource = PhoneBook.Entries;

            // 選択イベント
            _phoneBookList.SelectionChanged += (s, e) => {
                var selected = _phoneBookList.SelectedItem as PhoneBookEntry;
                if (selected != null)
                {
                    _statusText.Text = selected.Name;
                    _selectedNumberText.Text = selected.Number;
                    // TODO: ここで発信準備状態にする
                }
            };

            leftPanel.Children.Add(_phoneBookList);
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);


            // === 右側: 操作盤エリア ===
            var rightPanel = new Border
            {
                Margin = new Thickness(5, 15, 15, 15),
                Background = Brushes.White,
                CornerRadius = new CornerRadius(10),
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, Opacity = 0.1, BlurRadius = 10 }
            };

            var rightContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

            // 相手の名前表示
            _statusText = new TextBlock
            {
                Text = "待機中",
                FontSize = 24,
                FontWeight = FontWeights.Light,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            rightContent.Children.Add(_statusText);

            // 番号表示
            _selectedNumberText = new TextBlock
            {
                Text = "---",
                FontSize = 48,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 30),
                FontFamily = new FontFamily("Consolas")
            };
            rightContent.Children.Add(_selectedNumberText);

            // 発信ボタン
            var callBtn = new Button
            {
                Content = "発 信",
                Width = 200,
                Height = 60,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Background = Brushes.LightGray,
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // ボタンを丸くするスタイル
            var btnStyle = new Style(typeof(Border));
            btnStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(30)));
            callBtn.Resources.Add(typeof(Border), btnStyle);

            rightContent.Children.Add(callBtn);

            rightPanel.Child = rightContent;
            Grid.SetColumn(rightPanel, 1);
            mainGrid.Children.Add(rightPanel);

            dockPanel.Children.Add(mainGrid);
            Content = dockPanel;
        }

        private void OpenPropertySettings(object sender, RoutedEventArgs e)
        {
            var propWindow = new PropertyWindow(_currentInputDevice, _currentOutputDevice);
            propWindow.Owner = this;

            if (propWindow.ShowDialog() == true)
            {
                _currentInputDevice = propWindow.SelectedInput;
                _currentOutputDevice = propWindow.SelectedOutput;
            }
        }
    }
}