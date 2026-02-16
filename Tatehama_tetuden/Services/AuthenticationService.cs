using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using Tatehama_tetuden.Contracts;
using Tatehama_tetuden.Models;

namespace Tatehama_tetuden.Services
{
    /// <summary>
    /// OpenIddict を使用した認証サービス実装
    /// </summary>
    public class AuthenticationService : IAuthenticationService
    {
        private readonly TimeSpan _renewMargin = TimeSpan.FromMinutes(1);
        private readonly ILogger<AuthenticationService> _logger;
        private readonly OpenIddictClientService _openIddictClientService;
        private readonly IPhoneBookRepository _phoneBookRepo;

        private string _token = "";
        private string _refreshToken = "";
        private DateTimeOffset _tokenExpiration = DateTimeOffset.MinValue;

        public DateTime? TokenExpiration => _tokenExpiration != DateTimeOffset.MinValue
            ? _tokenExpiration.DateTime
            : null;

        public event Action? AuthenticationCompleted;
        public event Action<string>? AuthenticationFailed;

        public AuthenticationService(ILogger<AuthenticationService> logger, OpenIddictClientService openIddictClientService, IPhoneBookRepository phoneBookRepo)
        {
            _logger = logger;
            _openIddictClientService = openIddictClientService;
            _phoneBookRepo = phoneBookRepo;
        }

        /// <summary>
        /// ブラウザベースの OAuth 2.0 認証フローを実行
        /// </summary>
        public async Task<bool> AuthorizeAsync()
        {
            if (ServerAddress.IsDebug)
            {
                return true;
            }
            using var source = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                // ブラウザで認証要求
                var result = await _openIddictClientService.ChallengeInteractivelyAsync(new()
                {
                    CancellationToken = source.Token,
                    Scopes = [OpenIddictConstants.Scopes.OfflineAccess]
                });

                // 認証完了まで待機
                var resultAuth = await _openIddictClientService.AuthenticateInteractivelyAsync(new()
                {
                    CancellationToken = source.Token,
                    Nonce = result.Nonce
                });

                // 認証成功(トークン取得)
                _token = resultAuth.BackchannelAccessToken ?? "";
                _tokenExpiration = resultAuth.BackchannelAccessTokenExpirationDate ?? DateTimeOffset.MinValue;
                _refreshToken = resultAuth.RefreshToken ?? "";

                _logger.LogInformation("認証成功");
                AuthenticationCompleted?.Invoke();
                return true;
            }
            catch (OpenIddictExceptions.ProtocolException exception)
                when (exception.Error == OpenIddictConstants.Errors.AccessDenied)
            {
                string message = "認証が拒否されました。\n司令主任に連絡してください。";
                _logger.LogWarning(exception, "認証拒否: {Message}", message);
                AuthenticationFailed?.Invoke(message);
                return false;
            }
            catch (OpenIddictExceptions.ProtocolException exception)
                when (exception.Error == OpenIddictConstants.Errors.ServerError)
            {
                string message = "認証時にサーバーでエラーが発生しました。";
                _logger.LogError(exception, "サーバーエラー: {Message}", message);
                AuthenticationFailed?.Invoke(message);
                return false;
            }
            catch (Exception exception)
            {
                string message = $"認証に失敗しました: {exception.Message}";
                _logger.LogError(exception, "認証失敗: {Message}", message);
                AuthenticationFailed?.Invoke(message);
                return false;
            }
        }

        /// <summary>
        /// RefreshToken を使用してアクセストークンを更新
        /// </summary>
        public async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken))
            {
                _logger.LogDebug("リフレッシュトークン未設定");
                return false;
            }

            try
            {
                var result = await _openIddictClientService.AuthenticateWithRefreshTokenAsync(new()
                {
                    CancellationToken = CancellationToken.None,
                    RefreshToken = _refreshToken
                });

                _token = result.AccessToken ?? "";
                _tokenExpiration = result.AccessTokenExpirationDate ?? DateTimeOffset.MinValue;
                _refreshToken = result.RefreshToken ?? "";

                _logger.LogInformation("トークン更新成功");
                return true;
            }
            catch (OpenIddictExceptions.ProtocolException ex)
                when (ex.Error is OpenIddictConstants.Errors.InvalidToken
                                  or OpenIddictConstants.Errors.InvalidGrant
                                  or OpenIddictConstants.Errors.ExpiredToken)
            {
                _logger.LogWarning(ex, "リフレッシュトークンが無効または期限切れ");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "トークン更新中にエラー");
                return false;
            }
        }

        /// <summary>
        /// 現在の有効なアクセストークンを取得
        /// </summary>
        public string? GetAccessToken()
        {
            // トークンが有効期限内かチェック
            if (_tokenExpiration > DateTimeOffset.UtcNow + _renewMargin)
            {
                return _token;
            }

            // トークンが期限切れの場合は null を返す
            // （呼び出し側で RefreshToken を試みるべき）
            return null;
        }

        /// <summary>
        /// 認証後、ユーザーに許可された駅のリストを取得
        /// </summary>
        public async Task<List<PhoneBookEntry>> GetAllowedStationsAsync()
        {
            if (ServerAddress.IsDebug)
            {
                return _phoneBookRepo.GetAll();
            }

            // JWT トークンからクレームを読み取る
            var handler = new JwtSecurityTokenHandler();

            if (string.IsNullOrEmpty(_token))
            {
                _logger.LogWarning("トークン未取得");
                return new List<PhoneBookEntry>();
            }

            try
            {
                // 署名検証は行わない（意図的）。クライアント側でのJWT読み取りは
                // UIフィルタリング用途のみであり、サーバー側で権限検証を行う。
                var jwtToken = handler.ReadJwtToken(_token);

                // "role" クレームから許可された駅番号を取得
                // サーバー側で "role" クレームに "Station:XXX" の形式でロールを設定していると仮定
                var roleClaims = jwtToken.Claims
                    .Where(c => c.Type == "role" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                    .Select(c => c.Value)
                    .ToList();

                _logger.LogDebug("ユーザーロール: {Roles}", string.Join(", ", roleClaims));

                // "Station:XXX" のパターンから駅番号を抽出
                var allowedStationNumbers = roleClaims
                    .Where(r => r.StartsWith("Station:", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Substring("Station:".Length))
                    .ToList();

                // すべての駅を取得
                var allStations = await _phoneBookRepo.GetAllStationsAsync();

                // 許可された駅番号でフィルタリング
                if (allowedStationNumbers.Count > 0)
                {
                    var allowedStations = allStations
                        .Where(s => allowedStationNumbers.Contains(s.Number))
                        .ToList();

                    _logger.LogDebug("許可駅: {Stations}", string.Join(", ", allowedStations.Select(s => s.Name)));
                    return allowedStations;
                }
                else
                {
                    // ロールに駅が指定されていない場合、すべての駅を返す
                    // （または管理者権限を持つ可能性がある）
                    _logger.LogDebug("駅制限なし、全駅を許可");
                    return allStations;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JWTトークン解析エラー");
                // エラーの場合は全駅を返す（フェイルセーフ）
                return await _phoneBookRepo.GetAllStationsAsync();
            }
        }
    }
}
