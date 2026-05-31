using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

public class PlaylistService(
    AudioYotoShelfDbContext db,
    IAudiobookshelfService absService,
    ITrackPlanner planner,
    ICardCapacityCalculator calculator,
    ILogger<PlaylistService> logger) : IPlaylistService
{
    public async Task<PlaylistResponse> CreateAsync(
        Guid userConnectionId, CreatePlaylistRequest request, CancellationToken ct = default)
    {
        _ = await db.UserConnections.FindAsync([userConnectionId], ct)
            ?? throw new InvalidOperationException("User connection not found");

        var playlist = new Playlist
        {
            UserConnectionId = userConnectionId,
            Name = request.Name,
            DefaultGrouping = request.DefaultGrouping
        };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created playlist {PlaylistId} '{Name}' for user {UserId}",
            playlist.Id, playlist.Name, userConnectionId);
        return MapToResponse(playlist);
    }

    public async Task<PlaylistSummaryResponse[]> ListAsync(Guid userConnectionId, CancellationToken ct = default) =>
        await db.Playlists
            .Where(p => p.UserConnectionId == userConnectionId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PlaylistSummaryResponse(
                p.Id, p.Name, p.Status, p.Items.Count, p.YotoCardId, p.CreatedAt))
            .ToArrayAsync(ct);

    public async Task<PlaylistResponse?> GetAsync(Guid playlistId, CancellationToken ct = default)
    {
        var playlist = await LoadAsync(playlistId, ct);
        return playlist is null ? null : MapToResponse(playlist);
    }

    public async Task<PlaylistResponse> UpdateAsync(
        Guid playlistId, UpdatePlaylistRequest request, CancellationToken ct = default)
    {
        var playlist = await LoadOrThrowAsync(playlistId, ct);
        if (request.Name is not null) playlist.Name = request.Name;
        if (request.DefaultGrouping.HasValue) playlist.DefaultGrouping = request.DefaultGrouping.Value;
        await db.SaveChangesAsync(ct);
        return MapToResponse(playlist);
    }

    public async Task DeleteAsync(Guid playlistId, CancellationToken ct = default)
    {
        var playlist = await db.Playlists.FindAsync([playlistId], ct)
            ?? throw new InvalidOperationException("Playlist not found");
        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlaylistResponse> AddItemsAsync(
        Guid playlistId, IReadOnlyList<string> absLibraryItemIds, CancellationToken ct = default)
    {
        var playlist = await LoadOrThrowAsync(playlistId, ct);
        var user = await GetAbsUserAsync(playlist.UserConnectionId, ct);

        var nextPosition = playlist.Items.Count == 0 ? 0 : playlist.Items.Max(i => i.Position) + 1;

        foreach (var itemId in absLibraryItemIds.Distinct())
        {
            if (playlist.Items.Any(i => i.AbsLibraryItemId == itemId))
                continue; // already present

            var libraryItem = await absService.GetLibraryItemAsync(
                user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, ct);
            var media = libraryItem.Media;
            if (media is null)
            {
                logger.LogWarning("Skipping item {ItemId}: no media", itemId);
                continue;
            }

            var chapters = planner.Plan(media, TrackGrouping.Chapters); // raw per-chapter tracks
            var metadata = media.Metadata;

            var item = new PlaylistItem
            {
                PlaylistId = playlist.Id,
                AbsLibraryItemId = itemId,
                Position = nextPosition++,
                BookTitle = metadata.Title ?? "Unknown",
                BookAuthor = metadata.Authors?.FirstOrDefault()?.Name,
                SeriesName = metadata.Series?.FirstOrDefault()?.Name,
                SeriesSequence = ParseSequence(metadata.Series?.FirstOrDefault()?.Sequence),
                TrackDurations = chapters.Select(t => t.DurationSeconds).ToList(),
                TrackBytes = chapters.Select(t => t.EstimatedBytes).ToList()
            };
            // EF relationship fixup adds this to playlist.Items via the FK; don't add it twice.
            db.PlaylistItems.Add(item);
        }

        await db.SaveChangesAsync(ct);
        return MapToResponse(playlist);
    }

    public async Task<PlaylistResponse> AddSeriesAsync(
        Guid playlistId, string absSeriesId, CancellationToken ct = default)
    {
        var playlist = await LoadOrThrowAsync(playlistId, ct);
        var user = await GetAbsUserAsync(playlist.UserConnectionId, ct);

        if (string.IsNullOrEmpty(user.DefaultLibraryId))
            throw new InvalidOperationException("No default library set; cannot resolve series books.");

        var series = await absService.GetSeriesDetailAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, user.DefaultLibraryId, absSeriesId, ct);

        // Default an unnamed playlist to the series name (the user can still rename it).
        if (string.IsNullOrWhiteSpace(playlist.Name) && !string.IsNullOrWhiteSpace(series.Name))
        {
            playlist.Name = series.Name;
            await db.SaveChangesAsync(ct);
        }

        var orderedIds = series.Books
            .OrderBy(b => ParseSequence(b.Sequence) ?? 999f)
            .Select(b => b.Id)
            .ToArray();

        return await AddItemsAsync(playlistId, orderedIds, ct);
    }

    public async Task<PlaylistResponse> RemoveItemAsync(
        Guid playlistId, Guid itemId, CancellationToken ct = default)
    {
        var playlist = await LoadOrThrowAsync(playlistId, ct);
        var item = playlist.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is not null)
        {
            db.PlaylistItems.Remove(item);
            playlist.Items.Remove(item);
            Repack(playlist);
            await db.SaveChangesAsync(ct);
        }
        return MapToResponse(playlist);
    }

    public async Task<PlaylistResponse> ReorderAsync(
        Guid playlistId, IReadOnlyList<Guid> orderedItemIds, CancellationToken ct = default)
    {
        var playlist = await LoadOrThrowAsync(playlistId, ct);
        var position = 0;
        foreach (var id in orderedItemIds)
        {
            var item = playlist.Items.FirstOrDefault(i => i.Id == id);
            if (item is not null) item.Position = position++;
        }
        // Any items not named keep their relative order after the reordered ones.
        foreach (var item in playlist.Items.Where(i => !orderedItemIds.Contains(i.Id)).OrderBy(i => i.Position))
            item.Position = position++;

        await db.SaveChangesAsync(ct);
        return MapToResponse(playlist);
    }

    public async Task<PlaylistResponse> SetItemGroupingAsync(
        Guid playlistId, Guid itemId, TrackGrouping? groupingOverride, CancellationToken ct = default)
    {
        var playlist = await LoadOrThrowAsync(playlistId, ct);
        var item = playlist.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Playlist item not found");
        item.GroupingOverride = groupingOverride;
        await db.SaveChangesAsync(ct);
        return MapToResponse(playlist);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<Playlist?> LoadAsync(Guid playlistId, CancellationToken ct) =>
        await db.Playlists.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == playlistId, ct);

    private async Task<Playlist> LoadOrThrowAsync(Guid playlistId, CancellationToken ct) =>
        await LoadAsync(playlistId, ct) ?? throw new InvalidOperationException("Playlist not found");

    private async Task<UserConnection> GetAbsUserAsync(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct)
            ?? throw new InvalidOperationException("User connection not found");
        if (!user.HasValidAbsConnection)
            throw new InvalidOperationException("No valid Audiobookshelf connection");
        return user;
    }

    private static void Repack(Playlist playlist)
    {
        var position = 0;
        foreach (var item in playlist.Items.OrderBy(i => i.Position))
            item.Position = position++;
    }

    private PlaylistResponse MapToResponse(Playlist playlist)
    {
        var orderedItems = playlist.Items.OrderBy(i => i.Position).ToList();
        var plans = PlaylistCapacity.BuildPlans(playlist, planner);
        var capacity = calculator.Calculate(plans);

        var items = orderedItems.Select(i =>
        {
            var effective = i.GroupingOverride ?? playlist.DefaultGrouping;
            var grouped = PlaylistCapacity.GroupedTracks(playlist, i, planner);
            var projected = grouped.Sum(t => calculator.ProjectedTrackCount(t));
            return new PlaylistItemResponse(
                i.Id, i.AbsLibraryItemId, i.Position, i.BookTitle, i.BookAuthor,
                i.SeriesName, i.SeriesSequence, i.GroupingOverride, effective,
                projected, i.TrackDurations.Sum(), i.TrackBytes.Sum());
        }).ToArray();

        return new PlaylistResponse(
            playlist.Id, playlist.Name, playlist.Status, playlist.DefaultGrouping,
            playlist.YotoCardId, playlist.ErrorMessage, playlist.CreatedAt, playlist.TransferredAt,
            items, ToSummary(capacity));
    }

    private static CapacitySummary ToSummary(Core.DTOs.Playlist.CapacityResult r) => new(
        r.Projected.Tracks, r.Limits.MaxTracks,
        r.Projected.DurationSeconds, r.Limits.MaxTotalDurationSeconds,
        r.Projected.Bytes, r.Limits.MaxTotalBytes,
        r.WithinLimits, r.ExceedsTracks, r.ExceedsDuration, r.ExceedsBytes,
        r.FirstOverflowItemId, r.Messages.ToArray());

    internal static float? ParseSequence(string? sequence)
    {
        if (string.IsNullOrEmpty(sequence)) return null;
        return float.TryParse(sequence, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
    }
}
