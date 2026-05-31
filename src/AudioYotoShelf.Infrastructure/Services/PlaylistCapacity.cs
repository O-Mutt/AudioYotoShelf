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

    public static IReadOnlyList<SourceTrack> ChapterTracks(PlaylistItem item) =>
        item.TrackDurations
            .Select((duration, idx) => new SourceTrack(
                item.BookTitle,
                duration,
                idx < item.TrackBytes.Count ? item.TrackBytes[idx] : 0))
            .ToList();
}
