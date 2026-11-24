using AudioYotoShelf.Core.Configuration;

namespace AudioYotoShelf.Core.DTOs.Playlist;

/// <summary>A single logical track derived from a book's media (one file or one chapter).</summary>
public record SourceTrack(string Title, double DurationSeconds, long EstimatedBytes)
{
    /// <summary>
    /// Source bytes of the parts Audiobookshelf has not scanned yet (duration 0). Tracked apart from
    /// the duration so merging scanned and unscanned chapters into one track keeps both size signals.
    /// </summary>
    public long UnscannedBytes { get; init; } = DurationSeconds > 0 ? 0 : EstimatedBytes;
}

/// <summary>The ordered tracks that make up one book within a playlist.</summary>
public record BookTrackPlan(string AbsLibraryItemId, string BookTitle, IReadOnlyList<SourceTrack> Tracks);

/// <summary>Projected usage after per-track splitting is applied.</summary>
public record CapacityUsage(int Tracks, double DurationSeconds, long Bytes);

/// <summary>
/// Result of checking a set of books against the card limits.
/// Byte figures estimate Yoto's transcoded size from duration (see
/// <see cref="YotoCardLimits.TranscodedBitrateKbps"/>); track count and duration are exact.
/// </summary>
public record CapacityResult(
    CapacityUsage Projected,
    YotoCardLimits Limits,
    bool WithinLimits,
    bool ExceedsTracks,
    bool ExceedsDuration,
    bool ExceedsBytes,
    string? FirstOverflowItemId,
    IReadOnlyList<string> Messages);
