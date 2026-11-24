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

    // Set only when this item is one part of a book split across cards; null means the whole book.
    // Inclusive indices into TrackDurations, which then holds the book's full chapter list (for a
    // book without chapters, equal time segments).
    public int? FirstChapterIndex { get; set; }
    public int? LastChapterIndex { get; set; }
    public int? PartNumber { get; set; }
    public int? PartCount { get; set; }

    public bool IsPart => FirstChapterIndex is not null && LastChapterIndex is not null;
}
