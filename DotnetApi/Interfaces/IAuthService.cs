using DotnetApi.Contracts;

namespace DotnetApi.Interfaces;

public interface IAuthService
{
    Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken ct);

    Task<AuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct);

    Task LogoutAsync(string refreshToken, CancellationToken ct);

    Task<AuthUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct);
}
