using AudioYotoShelf.Core.Configuration;

namespace AudioYotoShelf.Core.DTOs.Playlist;

/// <summary>A single logical track derived from a book's media (one file or one chapter).</summary>
public record SourceTrack(string Title, double DurationSeconds, long EstimatedBytes);

/// <summary>The ordered tracks that make up one book within a playlist.</summary>
public record BookTrackPlan(string AbsLibraryItemId, string BookTitle, IReadOnlyList<SourceTrack> Tracks);

/// <summary>Projected usage after per-track splitting is applied.</summary>
public record CapacityUsage(int Tracks, double DurationSeconds, long Bytes);

/// <summary>
/// Result of checking a set of books against the card limits.
/// Byte figures are conservative estimates from source sizes; track count and
/// duration are exact and are the hard gates.
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
