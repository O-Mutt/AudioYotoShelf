using AudioYotoShelf.Api.Auth;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Hubs;

/// <summary>
/// SignalR hub for pushing real-time transfer progress updates to the Vue frontend.
/// Clients join a group per transfer ID to receive targeted updates. Requires an authenticated
/// session, and a client may only subscribe to transfers owned by its own connection.
/// </summary>
[Authorize]
public class TransferHub(AudioYotoShelfDbContext db) : Hub
{
    public async Task JoinTransferGroup(Guid transferId)
    {
        var owner = await db.CardTransfers
            .Where(t => t.Id == transferId)
            .Select(t => (Guid?)t.UserConnectionId)
            .FirstOrDefaultAsync();

        // Silently ignore attempts to watch a transfer that isn't yours.
        if (owner is null || owner != Context.User.GetUserConnectionId())
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, transferId.ToString());
    }

    public async Task LeaveTransferGroup(Guid transferId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, transferId.ToString());
    }

    /// <summary>Subscribes to a card set's sending progress; ignored for a set that isn't the caller's.</summary>
    public async Task JoinCardSetGroup(Guid cardSetId)
    {
        var owner = await db.CardSets
            .Where(s => s.Id == cardSetId)
            .Select(s => (Guid?)s.UserConnectionId)
            .FirstOrDefaultAsync();
        if (owner is null || owner != Context.User.GetUserConnectionId())
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, CardSetGroup(cardSetId));
    }

    public async Task LeaveCardSetGroup(Guid cardSetId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, CardSetGroup(cardSetId));
    }

    // Prefixed so a set's group can never collide with a transfer's, which is named by its bare id.
    internal static string CardSetGroup(Guid cardSetId) => $"cardset:{cardSetId}";
}

/// <summary>SignalR-backed implementation of <see cref="ICardSetProgressNotifier"/>.</summary>
public class SignalRCardSetProgressNotifier(IHubContext<TransferHub> hubContext) : ICardSetProgressNotifier
{
    public Task CardChangedAsync(CardSetProgressUpdate update, CancellationToken ct = default) =>
        hubContext.Clients
            .Group(TransferHub.CardSetGroup(update.CardSetId))
            .SendAsync("CardSetProgress", update, ct);

    public Task SendsChangedAsync(CancellationToken ct = default) =>
        hubContext.Clients.All.SendAsync("TransferListChanged", ct);
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

    public Task NotifyListChangedAsync(CancellationToken ct = default)
        => hubContext.Clients.All.SendAsync("TransferListChanged", ct);
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
