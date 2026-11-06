namespace DotnetApi.Interfaces;

public sealed record RefreshTokenRecord(Guid UserId, DateTimeOffset ExpiresAt);

public interface IRefreshTokenStore
{
    Task StoreAsync(
        string tokenHash,
        Guid userId,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    Task<RefreshTokenRecord?> GetAsync(string tokenHash, CancellationToken ct);

    Task RemoveAsync(string tokenHash, CancellationToken ct);
}
