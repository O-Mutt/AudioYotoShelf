using AudioYotoShelf.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace AudioYotoShelf.IntegrationTests;

/// <summary>
/// Boots the real API against throwaway Postgres + Redis containers, so the app's startup
/// migrations, cookie auth, ownership, admin gating, health checks, and metrics all run for real.
/// Only the external Audiobookshelf client is faked (see <see cref="FakeAudiobookshelfService"/>).
/// </summary>
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public FakeAudiobookshelfService Abs { get; } = new();

    /// <summary>The trusted ABS URL configured for admin promotion in these tests.</summary>
    public const string AdminAbsUrl = "http://abs.test";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("Yoto:ClientId", "test-client");
        builder.UseSetting("Admin:AudiobookshelfUrl", AdminAbsUrl);
        builder.UseSetting("Admin:Usernames", "adminuser");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAudiobookshelfService>();
            services.AddSingleton<IAudiobookshelfService>(Abs);
        });
    }
}
