using DotnetApi.Models;

namespace DotnetApi.Interfaces;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(AppUser user);

    string CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
