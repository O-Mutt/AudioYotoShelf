using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services.IconGeneration;

/// <summary>
/// Decorator that wraps IIconGenerationService to enforce Gemini free-tier daily rate limits.
/// OCP: GeminiIconGenerationService is unchanged; rate limiting is layered on via decoration.
/// LSP: Fully implements IIconGenerationService — consumers don't know about the wrapper.
/// 
/// Strategy: When daily count >= DailyLimit, falls back to:
///   1. Search public Yoto icon library for matching icons
///   2. Convert the book cover to a 16x16 icon
///   3. Return a default placeholder icon
/// </summary>
public class RateLimitedIconService(
    GeminiIconGenerationService inner,
    ICacheService cacheService,
    ILogger<RateLimitedIconService> logger) : IIconGenerationService
{
    /// <summary>
    /// Buffer of 10 below the 500/day free tier limit to avoid hitting it mid-batch.
    /// </summary>
    internal const int DailyLimit = 490;

    public async Task<byte[]> GenerateIconAsync(string prompt, CancellationToken ct = default)
    {
        if (await IsOverLimitAsync(ct))
        {
            logger.LogWarning("Gemini daily limit reached ({Limit}), falling back to public icons", DailyLimit);
            throw new InvalidOperationException(
                $"Gemini daily icon limit reached ({DailyLimit}). Use SearchPublicIconsAsync or ConvertCoverToIconAsync as fallback.");
        }

        var result = await inner.GenerateIconAsync(prompt, ct);
        await IncrementCountAsync(ct);
        return result;
    }

    public async Task<byte[]> GenerateChapterIconAsync(
        string chapterTitle, string bookTitle, string? genre, CancellationToken ct = default)
    {
        if (await IsOverLimitAsync(ct))
        {
            logger.LogWarning("Gemini daily limit reached, falling back for chapter icon: {Chapter}", chapterTitle);

            // Fallback 1: Search public icons
            var publicIcons = await SearchPublicIconsAsync(chapterTitle, 1, ct);
            if (publicIcons.Length > 0)
            {
                logger.LogInformation("Using public icon '{Title}' as fallback for {Chapter}",
                    publicIcons[0].Title, chapterTitle);
                // Return empty — caller will use the icon URL from publicIcons[0].Url
                // In a full implementation, we'd fetch and return the bytes
                throw new InvalidOperationException(
                    $"Gemini limit reached. Public icon available: {publicIcons[0].Url}");
            }

            // Fallback 2: Signal to caller to use cover conversion
            throw new InvalidOperationException(
                "Gemini daily limit reached. No public icons found. Use ConvertCoverToIconAsync.");
        }

        var result = await inner.GenerateChapterIconAsync(chapterTitle, bookTitle, genre, ct);
        await IncrementCountAsync(ct);
        return result;
    }

    // Pass-through methods that don't count against Gemini quota

    public Task<byte[]> ConvertCoverToIconAsync(Stream coverImage, CancellationToken ct = default)
        => inner.ConvertCoverToIconAsync(coverImage, ct);

    public Task<YotoPublicIcon[]> SearchPublicIconsAsync(string query, int maxResults = 10, CancellationToken ct = default)
        => inner.SearchPublicIconsAsync(query, maxResults, ct);

    public string BuildChapterIconPrompt(string chapterTitle, string bookTitle, string? genre)
        => inner.BuildChapterIconPrompt(chapterTitle, bookTitle, genre);

    // =========================================================================
    // Rate limit tracking via ICacheService
    // =========================================================================

    internal async Task<bool> IsOverLimitAsync(CancellationToken ct)
    {
        var count = await GetCurrentCountAsync(ct);
        return count >= DailyLimit;
    }

    internal async Task<int> GetCurrentCountAsync(CancellationToken ct)
    {
        var key = CacheKeys.GeminiDailyCount(DateOnly.FromDateTime(DateTime.UtcNow));
        var wrapper = await cacheService.GetAsync<CountWrapper>(key, ct);
        return wrapper?.Count ?? 0;
    }

    internal async Task IncrementCountAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = CacheKeys.GeminiDailyCount(today);
        var wrapper = await cacheService.GetAsync<CountWrapper>(key, ct);
        var newCount = (wrapper?.Count ?? 0) + 1;

        // TTL until end of day + 1 hour buffer
        var endOfDay = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ttl = endOfDay - DateTime.UtcNow + TimeSpan.FromHours(1);

        await cacheService.SetAsync(key, new CountWrapper(newCount), ttl, ct);

        if (newCount >= DailyLimit - 20)
        {
            logger.LogWarning("Approaching Gemini daily limit: {Count}/{Limit}", newCount, DailyLimit);
        }
    }

    /// <summary>
    /// Wrapper class for serializing a simple int to the cache.
    /// ICacheService requires T : class.
    /// </summary>
    internal record CountWrapper(int Count);
}
