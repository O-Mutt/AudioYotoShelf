using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Infrastructure.Data;

public class AudioYotoShelfDbContext(DbContextOptions<AudioYotoShelfDbContext> options)
    : DbContext(options)
{
    public DbSet<UserConnection> UserConnections => Set<UserConnection>();
    public DbSet<CardTransfer> CardTransfers => Set<CardTransfer>();
    public DbSet<TrackMapping> TrackMappings => Set<TrackMapping>();
    public DbSet<GeneratedIcon> GeneratedIcons => Set<GeneratedIcon>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<CardSet> CardSets => Set<CardSet>();
    public DbSet<LoginEvent> LoginEvents => Set<LoginEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AudioYotoShelfDbContext).Assembly);

        // Global query filter: soft-delete pattern ready if needed later
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Playlist>())
        {
            if ((entry.State == EntityState.Modified && entry.Property(p => p.Status).IsModified)
                || (entry.State == EntityState.Added && entry.Entity.Status != PlaylistStatus.Draft
                    && entry.Entity.StatusChangedAt is null))
            {
                entry.Entity.StatusChangedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
