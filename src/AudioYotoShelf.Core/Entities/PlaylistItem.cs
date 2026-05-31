using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// One book within a <see cref="Playlist"/>. Caches the book's track structure
/// (durations and estimated sizes) at add-time so capacity can be recomputed on
/// every edit without re-querying Audiobookshelf.
/// </summary>
public class PlaylistItem : BaseEntity
{
    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;

    public required string AbsLibraryItemId { get; set; }
    public int Position { get; set; }

    public required string BookTitle { get; set; }
    public string? BookAuthor { get; set; }
    public string? SeriesName { get; set; }
    public float? SeriesSequence { get; set; }

    /// <summary>Per-item grouping override; falls back to the playlist default when null.</summary>
    public TrackGrouping? GroupingOverride { get; set; }

    /// <summary>Per-source-track durations (seconds), cached from ABS metadata.</summary>
    public List<double> TrackDurations { get; set; } = [];

    /// <summary>Per-source-track estimated sizes (bytes), aligned with <see cref="TrackDurations"/>.</summary>
    public List<long> TrackBytes { get; set; } = [];
}
