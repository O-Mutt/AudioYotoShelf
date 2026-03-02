using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AudioYotoShelf.Api.Hubs;

/// <summary>
/// SignalR hub for pushing real-time transfer progress updates to the Vue frontend.
/// Clients join a group per transfer ID to receive targeted updates.
/// </summary>
public class TransferHub : Hub
{
	public async Task JoinTransferGroup(Guid transferId)
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, transferId.ToString());
	}

	public async Task LeaveTransferGroup(Guid transferId)
	{
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, transferId.ToString());
	}
}

/// <summary>
/// SignalR-backed implementation of ITransferProgressNotifier.
/// DIP: Infrastructure depends on the Core interface; this Api-layer class provides the concrete impl.
/// </summary>
public class SignalRTransferProgressNotifier(
		IHubContext<TransferHub> hubContext) : ITransferProgressNotifier
{
	public async Task SendProgressAsync(TransferProgressUpdate update, CancellationToken ct)
	{
		await hubContext.Clients
				.Group(update.TransferId.ToString())
				.SendAsync("TransferProgress", update, ct);
	}
}

/// <summary>
/// Extension methods for sending progress updates from background jobs.
/// </summary>
public static class TransferHubExtensions
{
	public static async Task SendProgressUpdateAsync(
			this IHubContext<TransferHub> hubContext,
			TransferProgressUpdate update)
	{
		await hubContext.Clients
				.Group(update.TransferId.ToString())
				.SendAsync("TransferProgress", update);
	}
}