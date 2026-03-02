using AudioYotoShelf.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Infrastructure.Tests.Fixtures;

/// <summary>
/// Provides a fresh InMemory database for each test.
/// Ensures test isolation without requiring a real PostgreSQL instance.
/// </summary>
public class InMemoryDbFixture : IDisposable
{
    public AudioYotoShelfDbContext DbContext { get; }

    public InMemoryDbFixture()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        DbContext = new AudioYotoShelfDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        DbContext.Database.EnsureDeleted();
        DbContext.Dispose();
    }
}
