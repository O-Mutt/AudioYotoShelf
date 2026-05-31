using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.DTOs.Playlist;

// --- Requests ---

public record CreatePlaylistRequest(string Name, TrackGrouping DefaultGrouping = TrackGrouping.Auto);

public record UpdatePlaylistRequest(string? Name = null, TrackGrouping? DefaultGrouping = null);

public record AddPlaylistItemsRequest(string[] AbsLibraryItemIds);

public record AddPlaylistSeriesRequest(string AbsSeriesId);

public record ReorderPlaylistRequest(Guid[] OrderedItemIds);

public record UpdatePlaylistItemRequest(TrackGrouping? GroupingOverride);

// --- Responses ---

public record CapacitySummary(
    int Tracks,
    int MaxTracks,
    double DurationSeconds,
    double MaxDurationSeconds,
    long Bytes,
    long MaxBytes,
    bool WithinLimits,
    bool ExceedsTracks,
    bool ExceedsDuration,
    bool ExceedsBytes,
    string? FirstOverflowItemId,
    string[] Messages);

public record PlaylistItemResponse(
    Guid Id,
    string AbsLibraryItemId,
    int Position,
    string BookTitle,
    string? BookAuthor,
    string? SeriesName,
    float? SeriesSequence,
    TrackGrouping? GroupingOverride,
    TrackGrouping EffectiveGrouping,
    int ProjectedTrackCount,
    double DurationSeconds,
    long EstimatedBytes);

public record PlaylistResponse(
    Guid Id,
    string Name,
    PlaylistStatus Status,
    TrackGrouping DefaultGrouping,
    string? YotoCardId,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TransferredAt,
    PlaylistItemResponse[] Items,
    CapacitySummary Capacity);

public record PlaylistSummaryResponse(
    Guid Id,
    string Name,
    PlaylistStatus Status,
    int ItemCount,
    string? YotoCardId,
    DateTimeOffset CreatedAt);
