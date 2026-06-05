using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AudioYotoShelf.Api.Health;

/// <summary>Readiness check: can we reach PostgreSQL?</summary>
public sealed class PostgresHealthCheck(AudioYotoShelfDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to PostgreSQL");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL check failed", ex);
        }
    }
}

/// <summary>Readiness check: round-trip a value through Redis.</summary>
public sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetStringAsync("health_check", "ok",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) },
                cancellationToken);
            var value = await cache.GetStringAsync("health_check", cancellationToken);
            return value == "ok"
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Redis round-trip mismatch");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis check failed", ex);
        }
    }
}

/// <summary>
/// FFmpeg powers single-file chapter extraction. If it's missing the app still serves and most
/// transfers work, so this reports Degraded (still a 200 for readiness) rather than Unhealthy.
/// </summary>
public sealed class FfmpegHealthCheck(IChapterExtractor ffmpeg) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var available = await ffmpeg.IsFfmpegAvailableAsync(cancellationToken);
        return available
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("FFmpeg not available");
    }
}
