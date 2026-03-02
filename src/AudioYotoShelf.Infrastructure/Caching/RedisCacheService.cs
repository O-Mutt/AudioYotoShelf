using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Caching;

/// <summary>
/// Typed Redis cache wrapper with serialization, TTL management,
/// and transparent cache-aside pattern support.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;
}

public class RedisCacheService(
    IDistributedCache cache,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var data = await cache.GetStringAsync(key, ct);
            if (data is null) return null;

            return JsonSerializer.Deserialize<T>(data, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
    {
        try
        {
            var data = JsonSerializer.Serialize(value, JsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
            };

            await cache.SetStringAsync(key, data, options, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache remove failed for key {Key}", key);
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null, CancellationToken ct = default) where T : class
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var value = await factory(ct);
        await SetAsync(key, value, ttl, ct);
        return value;
    }
}

/// <summary>
/// Cache key constants to avoid magic strings and ensure consistency.
/// </summary>
public static class CacheKeys
{
    public static string AbsLibraries(string userId) => $"abs:libraries:{userId}";
    public static string AbsLibraryItems(string userId, string libraryId, int page) =>
        $"abs:items:{userId}:{libraryId}:{page}";
    public static string AbsItem(string itemId) => $"abs:item:{itemId}";
    public static string AbsSeries(string libraryId, int page) => $"abs:series:{libraryId}:{page}";
    public const string YotoPublicIcons = "yoto:public_icons";
    public static string GeminiDailyCount(DateOnly date) => $"gemini:count:{date:yyyy-MM-dd}";
}
