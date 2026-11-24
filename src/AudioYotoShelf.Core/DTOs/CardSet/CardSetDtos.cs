using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.DTOs.CardSet;

// --- Requests ---

/// <summary>
/// Preview a plan for a series. <see cref="Breaks"/> (index of the first book of each card after the
/// first) and <see cref="SplitPoints"/> (per split book, index of the first chapter of each part
/// after the first) replace the automatic packing when given.
/// </summary>
public record CardSetPlanRequest(
    string AbsSeriesId,
    string AbsLibraryId,
    PackingMode Mode = PackingMode.Balance,
    string[]? ExcludedItemIds = null,
    int[]? Breaks = null,
    Dictionary<string, int[]>? SplitPoints = null);

public record CreateCardSetRequest(
    string AbsSeriesId,
    string AbsLibraryId,
    PackingMode Mode = PackingMode.Balance,
    string[]? ExcludedItemIds = null,
    int[]? Breaks = null,
    Dictionary<string, int[]>? SplitPoints = null,
    string? Name = null,
    string[]? CardTitles = null);

public record UpdateCardSetBreaksRequest(int[] Breaks, Dictionary<string, int[]>? SplitPoints = null);

public record UpdateCardSetRequest(string? Name = null, PackingMode? PackingMode = null, string[]? ExcludedItemIds = null);

public record RenameCardSetCardRequest(string Name);

/// <summary>Cards to send; null sends every card not yet on Yoto.</summary>
public record TransferCardSetRequest(Guid[]? PlaylistIds = null);

// --- Responses ---

public record CardSetChapterResponse(int Index, string Title, double DurationSeconds);

public record CardSetBookResponse(
    string AbsLibraryItemId,
    string Title,
    float? Sequence,
    double DurationSeconds,
    long EstimatedBytes,
    bool Split,
    CardSetChapterResponse[]? Chapters);

/// <summary>
/// One card. Plan previews leave the playlist fields null; a saved set fills them in.
/// <see cref="Locked"/> cards are on Yoto (or being sent) and never change.
/// </summary>
public record CardSetCardResponse(
    int Index,
    int FirstBookIndex,
    int LastBookIndex,
    int? FirstChapterIndex,
    int? LastChapterIndex,
    int? PartNumber,
    int? PartCount,
    string Title,
    CapacitySummary Capacity,
    Guid? PlaylistId = null,
    PlaylistStatus? Status = null,
    bool Locked = false,
    string? YotoCardId = null,
    string? ErrorMessage = null);

public record ExcludedBookResponse(string AbsLibraryItemId, string Title, float? Sequence);

public record CardSetPlanResponse(
    string SeriesName,
    CardSetBookResponse[] Books,
    ExcludedBookResponse[] ExcludedBooks,
    CardSetCardResponse[] Cards,
    bool WithinLimits,
    string[] UnsplittableBookIds,
    string[] Messages);

public record CardSetResponse(
    Guid Id,
    string Name,
    string AbsSeriesId,
    string AbsLibraryId,
    PackingMode PackingMode,
    string[] ExcludedItemIds,
    CardSetStatus Status,
    CardSetBookResponse[] Books,
    CardSetCardResponse[] Cards,
    bool WithinLimits,
    string[] Messages);

public record CardSetSummaryResponse(
    Guid Id,
    string Name,
    string AbsSeriesId,
    int CardCount,
    int SentCount,
    CardSetStatus Status);
