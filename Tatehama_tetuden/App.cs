using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Client;
using Tatehama_tetuden.Infrastructure;
using Tatehama_tetuden.Models;
using Tatehama_tetuden.Repositories;
using Tatehama_tetuden.Services;
using Tatehama_tetuden.View;

namespace Tatehama_tetuden
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
                    .ConfigureLogging(options =>
                    {
                        options.AddConsole();
                        options.AddDebug();
                    })
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
                                    Issuer = new Uri(ServerAddress.SignalAddress, UriKind.Absolute), // サーバーURL
                                    ClientId = "MultiATS_Client",
                                    RedirectUri = new Uri("/", UriKind.Relative)
                                });
                            });

                        // リポジトリの登録
                        services.AddSingleton<PhoneBookRepository>();
                        services.AddSingleton<IPhoneBookRepository>(sp => sp.GetRequiredService<PhoneBookRepository>());
                        services.AddSingleton<AudioDeviceRepository>();

                        // インフラ実装の登録
                        services.AddSingleton<IAuthenticationRepository, AuthenticationRepository>();
                        services.AddSingleton<ISignalingRepository, SignalingRepository>();
                        services.AddSingleton<IVoiceRepository, VoiceRepository>();
                        services.AddSingleton<ISoundRepository, SoundRepository>();
                        services.AddSingleton<CallService>();

                        // ウィンドウの登録
                        services.AddTransient<MainWindow>();
                    })
                    .Build();

                // ホスト起動（OpenIddict の SystemIntegration hosted service を含む）
                await _host.StartAsync();

                // SQLite DB の初期化（OpenIddict が使用するテーブルを作成）
                await using (var scope = _host.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<DbContext>();
                    await db.Database.EnsureCreatedAsync();
                }

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
                mainWindow.LoginRequested = TryAuthenticationFlowAsync;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();

                // AuthenticationFailed イベントをサブスクライブして UI でエラー表示
                var authService = _host.Services.GetRequiredService<IAuthenticationRepository>();
                authService.AuthenticationFailed += (message) =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(message, "認証エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                };

                // 2. バックグラウンドで認証を試みる
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500); // UI の初期化を待つ
                        await TryAuthenticationFlowAsync();
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show($"初期認証エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
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
                var authRepo = _host!.Services.GetRequiredService<IAuthenticationRepository>();

                // 認証処理を実行
                bool authSuccess = await authRepo.AuthorizeAsync();

                if (!authSuccess)
                {
                    // 認証失敗してもプログラムは継続
                    return;
                }

                // 認証成功後、駅選択画面を表示（UI スレッドで）
                await Dispatcher.InvokeAsync(async () =>
                {
                    var phoneBookRepo = _host.Services.GetRequiredService<IPhoneBookRepository>();

                    List<PhoneBookEntry> allowedStations;

                    if (ServerAddress.IsDebug)
                    {
                        allowedStations = phoneBookRepo.GetAll();
                    }
                    else
                    {
                        var roleClaims = authRepo.GetRoleClaims();
                        var allowedStationNumbers = roleClaims
                            .Where(r => r.StartsWith("Station:", StringComparison.OrdinalIgnoreCase))
                            .Select(r => r.Substring("Station:".Length))
                            .ToList();

                        var allStations = await phoneBookRepo.GetAllStationsAsync();

                        if (allowedStationNumbers.Count > 0)
                        {
                            allowedStations = allStations
                                .Where(s => allowedStationNumbers.Contains(s.Number))
                                .ToList();
                        }
                        else
                        {
                            allowedStations = allStations;
                        }
                    }

                    if (allowedStations.Count == 0)
                    {
                        MessageBox.Show("利用可能な駅がありません。管理者に連絡してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var selectionWindow = new StationSelectionWindow(allowedStations);
                    bool? result = selectionWindow.ShowDialog();

                    if (result == true && selectionWindow.SelectedStation != null)
                    {
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
