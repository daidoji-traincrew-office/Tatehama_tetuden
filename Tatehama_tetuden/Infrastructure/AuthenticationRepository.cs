using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using Tatehama_tetuden.Repositories;

namespace Tatehama_tetuden.Infrastructure;

public class AuthenticationRepository : IAuthenticationRepository
{
    private readonly TimeSpan _renewMargin = TimeSpan.FromMinutes(1);
    private readonly ILogger<AuthenticationRepository> _logger;
    private readonly OpenIddictClientService _openIddictClientService;

    private string _token = "";
    private string _refreshToken = "";
    private DateTimeOffset _tokenExpiration = DateTimeOffset.MinValue;

    public DateTime? TokenExpiration => _tokenExpiration != DateTimeOffset.MinValue
        ? _tokenExpiration.DateTime
        : null;

    public event Action? AuthenticationCompleted;
    public event Action<string>? AuthenticationFailed;

    public AuthenticationRepository(ILogger<AuthenticationRepository> logger, OpenIddictClientService openIddictClientService)
    {
        _logger = logger;
        _openIddictClientService = openIddictClientService;
    }

    public async Task<bool> AuthorizeAsync()
    {
        if (ServerAddress.IsDebug)
        {
            return true;
        }
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var result = await _openIddictClientService.ChallengeInteractivelyAsync(new()
            {
                CancellationToken = source.Token,
                Scopes = [OpenIddictConstants.Scopes.OfflineAccess]
            });

            var resultAuth = await _openIddictClientService.AuthenticateInteractivelyAsync(new()
            {
                CancellationToken = source.Token,
                Nonce = result.Nonce
            });

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

    public string? GetAccessToken()
    {
        if (_tokenExpiration > DateTimeOffset.UtcNow + _renewMargin)
        {
            return _token;
        }

        return null;
    }

    public List<string> GetRoleClaims()
    {
        if (ServerAddress.IsDebug || string.IsNullOrEmpty(_token))
        {
            return new List<string>();
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(_token);

            return jwtToken.Claims
                .Where(c => c.Type == "role" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                .Select(c => c.Value)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWTトークン解析エラー");
            return new List<string>();
        }
    }
}
