using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Series card sets: plan a series onto cards, save the plan as one playlist per card, change it
/// around the cards already on Yoto, and pick the cards to send (docs/specs/series-card-sets.md §7).
/// </summary>
public interface ICardSetService
{
    Task<CardSetPlanResponse> PlanAsync(Guid userConnectionId, CardSetPlanRequest request, CancellationToken ct = default);

    /// <exception cref="CardSetConflictException">The user already has a set for this series.</exception>
    /// <exception cref="CardSetPlanException">The requested breaks do not fit.</exception>
    Task<CardSetResponse> CreateAsync(Guid userConnectionId, CreateCardSetRequest request, CancellationToken ct = default);

    Task<CardSetSummaryResponse[]> ListAsync(Guid userConnectionId, string? absSeriesId = null, CancellationToken ct = default);

    /// <param name="withNewBooks">Also read the series from Audiobookshelf to list books new since the set was made.</param>
    Task<CardSetResponse?> GetAsync(Guid cardSetId, bool withNewBooks = true, CancellationToken ct = default);

    /// <summary>Card-set state for each of the given series that has a set, for the series grid.</summary>
    Task<CardSetStatusResponse[]> StatusAsync(Guid userConnectionId, IReadOnlyList<string> absSeriesIds, CancellationToken ct = default);

    /// <summary>
    /// The user's sets with a card that was ever queued or sent, latest activity first, for the
    /// Transfers page. <paramref name="status"/> keeps only sets in that state. A complete set with
    /// no activity in the recent window is counted but left out unless <paramref name="includeOlder"/>.
    /// </summary>
    Task<CardSetSendsResponse> SendsAsync(
        Guid userConnectionId, CardSetStatus? status = null, bool includeOlder = false, CancellationToken ct = default);

    CardLimitsResponse Limits();

    /// <summary>
    /// Re-reads the series and places books new since the set was made: they join the last card if it
    /// is not on Yoto, and new cards after it. Earlier cards stay as they are; a new book whose place in
    /// the series is before them goes on the first card that can still change, and a message says so.
    /// </summary>
    /// <exception cref="CardSetPlanException">A new book cannot be split onto cards; leave it out first.</exception>
    Task<CardSetResponse> RefreshAsync(Guid cardSetId, CancellationToken ct = default);

    /// <exception cref="CardSetConflictException">A break or split point moves a card already on Yoto.</exception>
    /// <exception cref="CardSetPlanException">A card would not fit.</exception>
    Task<CardSetResponse> UpdateBreaksAsync(Guid cardSetId, UpdateCardSetBreaksRequest request, CancellationToken ct = default);

    /// <summary>Renames the set, or re-packs the cards not yet on Yoto when the mode or exclusions change.</summary>
    Task<CardSetResponse> UpdateAsync(Guid cardSetId, UpdateCardSetRequest request, CancellationToken ct = default);

    Task<CardSetResponse> RenameCardAsync(Guid cardSetId, Guid playlistId, string name, CancellationToken ct = default);

    /// <summary>Deletes the set; its cards stay as standalone playlists.</summary>
    Task DeleteAsync(Guid cardSetId, CancellationToken ct = default);

    /// <summary>
    /// Marks the cards to send as queued and returns them in set order: the given ones, or every card
    /// that is not on Yoto, queued or being sent. Queuing them here is what stops a second Send from
    /// picking up cards an earlier job has not reached yet.
    /// </summary>
    /// <exception cref="CardSetCapacityException">A requested card is over the card limits.</exception>
    /// <exception cref="CardSetConflictException">A requested card is queued or being sent already.</exception>
    Task<Guid[]> PrepareTransferAsync(Guid cardSetId, IReadOnlyList<Guid>? playlistIds, CancellationToken ct = default);

    /// <summary>Puts cards that were queued back to draft, for when the send job could not be enqueued.</summary>
    Task ReleaseQueuedAsync(IReadOnlyList<Guid> playlistIds, CancellationToken ct = default);
}

/// <summary>409: the set already exists, or a change would move a card already on Yoto.</summary>
public class CardSetConflictException(string message, Guid? cardSetId = null) : Exception(message)
{
    public Guid? CardSetId { get; } = cardSetId;
}

/// <summary>422: the requested plan has a card that does not fit.</summary>
public class CardSetPlanException(string message, CardSetPlanResponse plan) : Exception(message)
{
    public CardSetPlanResponse Plan { get; } = plan;
}

/// <summary>409: a card asked to be sent is over the card limits.</summary>
public class CardSetCapacityException(string message, Guid playlistId, CapacitySummary capacity) : Exception(message)
{
    public Guid PlaylistId { get; } = playlistId;
    public CapacitySummary Capacity { get; } = capacity;
}
