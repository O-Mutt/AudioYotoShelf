using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Derives the logical track list from a book's media metadata without any I/O.
/// Mirrors the branching used by the transfer pipeline (multi-file, single-file+chapters,
/// single-file) so capacity projections match what actually gets uploaded, and applies the
/// requested <see cref="TrackGrouping"/> policy.
/// </summary>
public interface ITrackPlanner
{
    IReadOnlyList<SourceTrack> Plan(AbsBookMedia media, TrackGrouping grouping = TrackGrouping.Chapters);

    /// <summary>
    /// Applies a grouping policy to an already-derived list of per-chapter tracks. Used to
    /// recompute capacity from cached track data (durations/sizes) without re-querying ABS.
    /// </summary>
    IReadOnlyList<SourceTrack> ApplyGrouping(
        IReadOnlyList<SourceTrack> chapterTracks, TrackGrouping grouping, string bookTitle);
}
