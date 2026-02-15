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
        /// アプリケーションの初期化フロー: 認証 → 駅選択 → MainWindow 表示
        /// </summary>
        private async Task InitializeApplicationAsync()
        {
            try
            {
                // 1. 認証処理を実行
                var authService = _host!.Services.GetRequiredService<IAuthenticationService>();
                bool authSuccess = await authService.AuthorizeAsync();

                if (!authSuccess)
                {
                    MessageBox.Show("認証に失敗しました。アプリケーションを終了します。", "認証失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                // 2. 認証成功後、駅選択画面を表示
                var phoneBookRepo = _host.Services.GetRequiredService<PhoneBookRepository>();
                var allowedStations = await authService.GetAllowedStationsAsync(phoneBookRepo);

                if (allowedStations.Count == 0)
                {
                    MessageBox.Show("利用可能な駅がありません。管理者に連絡してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                var selectionWindow = new StationSelectionWindow(allowedStations);
                bool? result = selectionWindow.ShowDialog();

                if (result == true)
                {
                    // 3. 駅選択後、MainWindow を初期化して表示
                    var selectedStation = selectionWindow.SelectedStation;
                    if (selectedStation == null)
                    {
                        selectedStation = new PhoneBookEntry { Name = "緊急用予備端末", Number = "999" };
                    }

                    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    await mainWindow.InitializeWithStationAsync(selectedStation);

                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                    mainWindow.Show();
                }
                else
                {
                    // キャンセルされたら終了
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初期化エラー: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
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
