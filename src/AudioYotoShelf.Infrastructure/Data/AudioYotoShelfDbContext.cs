using AudioYotoShelf.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Infrastructure.Data;

public class AudioYotoShelfDbContext(DbContextOptions<AudioYotoShelfDbContext> options)
    : DbContext(options)
{
    public DbSet<UserConnection> UserConnections => Set<UserConnection>();
    public DbSet<CardTransfer> CardTransfers => Set<CardTransfer>();
    public DbSet<TrackMapping> TrackMappings => Set<TrackMapping>();
    public DbSet<GeneratedIcon> GeneratedIcons => Set<GeneratedIcon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AudioYotoShelfDbContext).Assembly);

        // Global query filter: soft-delete pattern ready if needed later
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
