using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Client;

namespace RailwayPhone
{
    /// <summary>
    /// アプリケーションクラス - IHost ベースの DI コンテナをセットアップ
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // IHost の初期化
                _host = new HostBuilder()
                    .ConfigureLogging(options => options.AddDebug())
                    .ConfigureServices(services =>
                    {
                        // DbContext の設定（OpenIddict が内部状態を保存するために必要）
                        services.AddDbContext<DbContext>(options =>
                        {
                            options.UseSqlite($"Filename={Path.Combine(Path.GetTempPath(), "railway-phone-auth.sqlite3")}");
                            options.UseOpenIddict();
                        });

                        // OpenIddict の設定
                        services.AddOpenIddict()
                            .AddCore(options =>
                            {
                                options.UseEntityFrameworkCore()
                                    .UseDbContext<DbContext>();
                            })
                            .AddClient(options =>
                            {
                                options.AllowAuthorizationCodeFlow()
                                    .AllowRefreshTokenFlow();

                                options.AddDevelopmentEncryptionCertificate()
                                    .AddDevelopmentSigningCertificate();

                                options.UseSystemIntegration();
                                options.UseSystemNetHttp()
                                    .SetProductInformation(typeof(App).Assembly);

                                options.AddRegistration(new OpenIddictClientRegistration
                                {
                                    Issuer = new Uri("http://127.0.0.1:8888/", UriKind.Absolute), // サーバーURL
                                    ClientId = "railway-phone-client",
                                    RedirectUri = new Uri("/", UriKind.Relative)
                                });
                            });

                        // リポジトリの登録
                        services.AddSingleton<PhoneBookRepository>();
                        services.AddSingleton<AudioDeviceRepository>();

                        // サービスの登録
                        services.AddSingleton<ISignalingService, SignalingService>();
                        services.AddSingleton<IVoiceService, VoiceService>();
                        services.AddSingleton<ISoundService, SoundService>();
                        services.AddSingleton<IAuthenticationService, AuthenticationService>();
                        services.AddSingleton<CallService>();

                        // ウィンドウの登録
                        services.AddTransient<MainWindow>();
                    })
                    .Build();

                // 認証とウィンドウ表示の処理
                await InitializeApplicationAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"起動エラー: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// アプリケーションの初期化フロー: MainWindow 表示 → バックグラウンドで認証
        /// </summary>
        private async Task InitializeApplicationAsync()
        {
            try
            {
                // 1. MainWindow を先に表示
                var mainWindow = _host!.Services.GetRequiredService<MainWindow>();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();

                // 2. バックグラウンドで認証を試みる
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500); // UI の初期化を待つ
                    await TryAuthenticationFlowAsync();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初期化エラー: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 認証フローを実行（起動時またはログインボタンから呼ばれる）
        /// </summary>
        public async Task TryAuthenticationFlowAsync()
        {
            try
            {
                var authService = _host!.Services.GetRequiredService<IAuthenticationService>();
                var phoneBookRepo = _host.Services.GetRequiredService<PhoneBookRepository>();

                // 認証処理を実行
                bool authSuccess = await authService.AuthorizeAsync();

                if (!authSuccess)
                {
                    // 認証失敗してもプログラムは継続
                    return;
                }

                // 認証成功後、駅選択画面を表示（UI スレッドで）
                await Dispatcher.InvokeAsync(async () =>
                {
                    var allowedStations = await authService.GetAllowedStationsAsync(phoneBookRepo);

                    if (allowedStations.Count == 0)
                    {
                        MessageBox.Show("利用可能な駅がありません。管理者に連絡してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var selectionWindow = new StationSelectionWindow(allowedStations);
                    bool? result = selectionWindow.ShowDialog();

                    if (result == true && selectionWindow.SelectedStation != null)
                    {
                        // MainWindow を取得して初期化
                        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                        await mainWindow.InitializeWithStationAsync(selectionWindow.SelectedStation);
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"認証エラー: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
                await _host.StopAsync();

            base.OnExit(e);
        }
    }
}
