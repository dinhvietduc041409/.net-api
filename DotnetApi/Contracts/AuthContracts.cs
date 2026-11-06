using DotnetApi.Infrastructure;
using DotnetApi.Models;

namespace DotnetApi.Contracts;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshTokenRequest(string? RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record AuthUserResponse(
    Guid Id,
    string Email,
    string Name,
    UserRole Role,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    AuthUserResponse User);

public static class AuthMappings
{
    public static AuthUserResponse ToAuthResponse(this AppUser user) => new(
        user.Id,
        user.Email,
        user.Name,
        user.Role,
        [user.Role.ToString().ToLowerInvariant()],
        AuthPermissions.For(user.Role));
}
