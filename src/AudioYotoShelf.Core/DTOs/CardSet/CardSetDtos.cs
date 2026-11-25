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

/// <summary>A series book named without its tracks: one left out of the plan, or one new to the series.</summary>
public record SeriesBookRefResponse(string AbsLibraryItemId, string Title, float? Sequence);

public record CardSetPlanResponse(
    string SeriesName,
    CardSetBookResponse[] Books,
    SeriesBookRefResponse[] ExcludedBooks,
    CardSetCardResponse[] Cards,
    bool WithinLimits,
    string[] UnsplittableBookIds,
    string[] Messages);

/// <summary>
/// A saved set. <see cref="NewBooks"/> lists series books in Audiobookshelf that are on no card and
/// not left out; it is null when the server did not check (every response but a read).
/// </summary>
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
    string[] Messages,
    SeriesBookRefResponse[]? NewBooks = null);

public record CardSetSummaryResponse(
    Guid Id,
    string Name,
    string AbsSeriesId,
    int CardCount,
    int SentCount,
    CardSetStatus Status);

/// <summary>One card of a set on the Transfers page, with the titles of the books it holds in card order.</summary>
public record CardSetSendCardResponse(
    Guid PlaylistId,
    int Index,
    string Title,
    PlaylistStatus Status,
    string? YotoCardId,
    string? ErrorMessage,
    DateTimeOffset? TransferredAt,
    string[] BookTitles);

/// <summary>
/// A set that has been sent, or is being sent, for the Transfers page. <see cref="LastActivityAt"/> is
/// the latest status change of any of its cards that is not a draft.
/// </summary>
public record CardSetSendResponse(
    Guid Id,
    string Name,
    string AbsSeriesId,
    string AbsLibraryId,
    CardSetStatus Status,
    int CardCount,
    int SentCount,
    DateTimeOffset LastActivityAt,
    CardSetSendCardResponse[] Cards);

/// <summary>
/// The Transfers page's card sets. <see cref="OlderCount"/> is how many complete sets with no activity
/// in the recent window match the filter; they are in <see cref="Sets"/> only when asked for.
/// </summary>
public record CardSetSendsResponse(CardSetSendResponse[] Sets, int OlderCount);

/// <summary>
/// Card-set state for one series, for the series grid. <see cref="KnownBookIds"/> are the books on
/// the set's cards or left out of it; any other book of the series is new since the set was made.
/// </summary>
public record CardSetStatusResponse(
    string AbsSeriesId,
    Guid CardSetId,
    int CardCount,
    int SentCount,
    bool Sending,
    CardSetStatus Status,
    string[] KnownBookIds);

/// <summary>The card limits in force, so the client can tell whether a series fits one card.</summary>
public record CardLimitsResponse(
    int MaxTracks,
    long MaxTotalBytes,
    double? MaxTotalDurationSeconds,
    int TranscodedBitrateKbps);

/// <summary>Pushed to a set's SignalR group whenever one of its cards changes state while sending.</summary>
public record CardSetProgressUpdate(
    Guid CardSetId,
    Guid PlaylistId,
    int? CardIndex,
    PlaylistStatus Status,
    string? ErrorMessage);
