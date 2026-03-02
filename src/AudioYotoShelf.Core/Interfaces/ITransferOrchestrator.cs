using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Entities;

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
    Task<TransferResponse> TransferBookAsync(Guid userConnectionId, CreateTransferRequest request, CancellationToken ct = default);

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
