using System.Text.Json;
using DotnetApi.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace DotnetApi.Services;

public sealed class DistributedRefreshTokenStore(IDistributedCache cache) : IRefreshTokenStore
{
    private const string KeyPrefix = "auth:refresh:";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task StoreAsync(
        string tokenHash,
        Guid userId,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        var lifetime = expiresAt - DateTimeOffset.UtcNow;

        if (lifetime <= TimeSpan.Zero)
        {
            lifetime = TimeSpan.FromSeconds(1);
        }

        var record = new RefreshTokenRecord(userId, expiresAt);

        return cache.SetStringAsync(
            KeyPrefix + tokenHash,
            JsonSerializer.Serialize(record, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime,
            },
            ct);
    }

    public async Task<RefreshTokenRecord?> GetAsync(string tokenHash, CancellationToken ct)
    {
        var value = await cache.GetStringAsync(KeyPrefix + tokenHash, ct);

        return string.IsNullOrEmpty(value)
            ? null
            : JsonSerializer.Deserialize<RefreshTokenRecord>(value, JsonOptions);
    }

    public Task RemoveAsync(string tokenHash, CancellationToken ct) =>
        cache.RemoveAsync(KeyPrefix + tokenHash, ct);
}
