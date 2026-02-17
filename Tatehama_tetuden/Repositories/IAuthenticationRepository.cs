namespace Tatehama_tetuden.Repositories;

public interface IAuthenticationRepository
{
    Task<bool> AuthorizeAsync();
    Task<bool> RefreshTokenAsync();
    string? GetAccessToken();
    List<string> GetRoleClaims();
    DateTime? TokenExpiration { get; }

    event Action? AuthenticationCompleted;
    event Action<string>? AuthenticationFailed;
}
