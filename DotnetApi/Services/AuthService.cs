using DotnetApi.Contracts;
using DotnetApi.Data;
using DotnetApi.Infrastructure;
using DotnetApi.Interfaces;
using DotnetApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;

namespace DotnetApi.Services;

public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokens,
    IOptions<JwtOptions> options) : IAuthService
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email!.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(user => user.Email == email, ct);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        var result = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password!);

        if (result == PasswordVerificationResult.Failed)
        {   
            System.Console.WriteLine(user);
            throw new InvalidCredentialsException();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var tokenHash = tokenService.HashRefreshToken(refreshToken);
        var record = await refreshTokens.GetAsync(tokenHash, ct);

        if (record is null)
        {
            throw new InvalidCredentialsException("Invalid or expired refresh token.");
        }

        // Rotate: the old refresh token can never be used again.
        await refreshTokens.RemoveAsync(tokenHash, ct);

        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == record.UserId, ct);

        if (user is null)
        {
            throw new InvalidCredentialsException("Invalid or expired refresh token.");
        }

        return await IssueTokensAsync(user, ct);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken ct) =>
        refreshTokens.RemoveAsync(tokenService.HashRefreshToken(refreshToken), ct);

    public async Task<AuthUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, ct);

        if (user is null)
        {
            throw new InvalidCredentialsException("The authenticated user no longer exists.");
        }

        return user.ToAuthResponse();
    }

    private async Task<AuthTokenResponse> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken();
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays);

        await refreshTokens.StoreAsync(
            tokenService.HashRefreshToken(refreshToken),
            user.Id,
            refreshExpiresAt,
            ct);

        return new AuthTokenResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken,
            user.ToAuthResponse());
    }
}
