using AudioYotoShelf.Core.DTOs.Transfer;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Orchestrates the full transfer pipeline from Audiobookshelf to Yoto.
/// Coordinates between ABS download, Yoto upload, icon generation, and card creation.
/// </summary>
public interface ITransferOrchestrator
{
    /// <summary>
    /// Transfer a single audiobook to a Yoto MYO card.
    /// </summary>
    Task<TransferResponse> TransferBookAsync(Guid userConnectionId, CreateTransferRequest request, Guid? transferId = null, CancellationToken ct = default);

    /// <summary>
    /// Checks a book against the card limits before it is queued, the same way the transfer job does.
    /// Returns why it can't go on one card, or null when it fits.
    /// </summary>
    Task<CapacityRefusal?> CheckBookCapacityAsync(Guid userConnectionId, string absLibraryItemId, CancellationToken ct = default);

    /// <summary>
    /// Transfer all books in a series, each to their own card or combined into one playlist.
    /// </summary>
    Task<TransferResponse[]> TransferSeriesAsync(Guid userConnectionId, CreateSeriesTransferRequest request, CancellationToken ct = default);

    /// <summary>
    /// Retry a previously failed transfer.
    /// </summary>
    Task<TransferResponse> RetryTransferAsync(Guid transferId, CancellationToken ct = default);

    /// <summary>
    /// Cancel an in-progress transfer.
    /// </summary>
    Task CancelTransferAsync(Guid transferId, CancellationToken ct = default);

    /// <summary>
    /// Get the current status of a transfer.
    /// </summary>
    Task<TransferResponse> GetTransferStatusAsync(Guid transferId, CancellationToken ct = default);

    /// <summary>
    /// Get all transfers for a user.
    /// </summary>
    Task<TransferResponse[]> GetUserTransfersAsync(Guid userConnectionId, int page = 0, int limit = 20, CancellationToken ct = default);
}

/// <summary>A book is over the card limits, so its transfer stops before any download.</summary>
public class CardCapacityExceededException(CapacityRefusal refusal, TransferResponse? transfer = null)
    : InvalidOperationException(refusal.Message)
{
    public CapacityRefusal Refusal { get; } = refusal;

    /// <summary>
    /// The transfer as it was marked Failed, so a caller can report it without reading it back; the
    /// person may delete it in the meantime.
    /// </summary>
    public TransferResponse? Transfer { get; } = transfer;
}
