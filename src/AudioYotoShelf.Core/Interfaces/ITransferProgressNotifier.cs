using AudioYotoShelf.Core.DTOs.Transfer;

namespace AudioYotoShelf.Core.Interfaces;

public interface ITransferProgressNotifier
{
    Task SendProgressAsync(TransferProgressUpdate update, CancellationToken ct);

    /// <summary>
    /// Broadcasts to all clients that the transfer list changed (e.g. a new transfer was
    /// queued), so views showing the list can refresh without a manual reload.
    /// </summary>
    Task NotifyListChangedAsync(CancellationToken ct = default);
}
