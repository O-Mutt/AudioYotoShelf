namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Transfers an entire playlist to a single Yoto MYO card: one chapter per book,
/// applying the per-book grouping policy and splitting oversized tracks to fit the
/// card limits. Throws if the playlist exceeds capacity.
/// </summary>
public interface IPlaylistTransferOrchestrator
{
    Task TransferPlaylistAsync(Guid playlistId, CancellationToken ct = default);
}
