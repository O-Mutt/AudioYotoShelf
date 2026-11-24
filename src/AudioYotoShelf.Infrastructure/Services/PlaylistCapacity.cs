using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Interfaces;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>
/// Builds capacity <see cref="BookTrackPlan"/>s from a persisted playlist's cached track data,
/// applying each item's effective grouping. Shared by the playlist service (preview) and the
/// transfer orchestrator (pre-flight capacity gate) so both agree on the projection.
/// </summary>
public static class PlaylistCapacity
{
    public static IReadOnlyList<BookTrackPlan> BuildPlans(Playlist playlist, ITrackPlanner planner) =>
        playlist.Items
            .OrderBy(i => i.Position)
            .Select(i => new BookTrackPlan(i.AbsLibraryItemId, i.BookTitle, GroupedTracks(playlist, i, planner)))
            .ToList();

    public static IReadOnlyList<SourceTrack> GroupedTracks(Playlist playlist, PlaylistItem item, ITrackPlanner planner)
    {
        var effective = item.GroupingOverride ?? playlist.DefaultGrouping;
        return planner.ApplyGrouping(ChapterTracks(item), effective, item.BookTitle);
    }

    /// <summary>
    /// The item's per-chapter tracks: the whole book, or for one part of a split book only the
    /// chapters in its range.
    /// </summary>
    public static IReadOnlyList<SourceTrack> ChapterTracks(PlaylistItem item)
    {
        var all = item.TrackDurations
            .Select((duration, idx) => new SourceTrack(
                item.BookTitle,
                duration,
                idx < item.TrackBytes.Count ? item.TrackBytes[idx] : 0))
            .ToList();
        if (!item.IsPart) return all;

        var first = Math.Clamp(item.FirstChapterIndex!.Value, 0, all.Count);
        var last = Math.Clamp(item.LastChapterIndex!.Value, first - 1, all.Count - 1);
        return all.GetRange(first, last - first + 1);
    }

    public static CapacitySummary ToSummary(CapacityResult r) => new(
        r.Projected.Tracks, r.Limits.MaxTracks,
        r.Projected.DurationSeconds, r.Limits.MaxTotalDurationSeconds,
        r.Projected.Bytes, r.Limits.MaxTotalBytes,
        r.WithinLimits, r.ExceedsTracks, r.ExceedsDuration, r.ExceedsBytes,
        r.FirstOverflowItemId, r.Messages.ToArray());
}
