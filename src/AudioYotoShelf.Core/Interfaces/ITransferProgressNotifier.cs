using AudioYotoShelf.Core.DTOs.Transfer;

namespace AudioYotoShelf.Core.Interfaces;

public interface ITransferProgressNotifier
{
	Task SendProgressAsync(TransferProgressUpdate update, CancellationToken ct);
}
