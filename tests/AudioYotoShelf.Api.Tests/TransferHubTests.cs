using System.Security.Claims;
using AudioYotoShelf.Api.Hubs;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class TransferHubTests : IDisposable
{
    private const string ConnectionId = "conn-1";

    private readonly AudioYotoShelfDbContext _db;
    private readonly Mock<IGroupManager> _groups = new();

    public TransferHubTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"HubTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private TransferHub HubFor(Guid callerConnectionId)
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns(ConnectionId);
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, callerConnectionId.ToString())], "Test")));
        return new TransferHub(_db) { Context = context.Object, Groups = _groups.Object };
    }

    private async Task<Guid> AddTransferOwnedByAsync(Guid ownerId)
    {
        var transfer = TestData.CreateCardTransfer(ownerId);
        _db.CardTransfers.Add(transfer);
        await _db.SaveChangesAsync();
        return transfer.Id;
    }

    // =========================================================================
    // Joining: only your own transfer's progress
    // =========================================================================

    [Fact]
    public async Task JoinTransferGroup_ForOwnTransfer_AddsTheConnectionToThatTransfersGroup()
    {
        var caller = Guid.NewGuid();
        var transferId = await AddTransferOwnedByAsync(caller);

        await HubFor(caller).JoinTransferGroup(transferId);

        _groups.Verify(g => g.AddToGroupAsync(ConnectionId, transferId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinTransferGroup_ForAnotherUsersTransfer_DoesNotJoin()
    {
        var transferId = await AddTransferOwnedByAsync(Guid.NewGuid());

        await HubFor(Guid.NewGuid()).JoinTransferGroup(transferId);

        _groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinTransferGroup_ForUnknownTransfer_DoesNotJoin()
    {
        await AddTransferOwnedByAsync(Guid.NewGuid());

        await HubFor(Guid.NewGuid()).JoinTransferGroup(Guid.NewGuid());

        _groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinTransferGroup_WhenTheCallerHasNoConnectionIdClaim_DoesNotJoinAnyTransfer()
    {
        // A principal with no id claim must not match a transfer whose owner is somehow unset.
        var transferId = await AddTransferOwnedByAsync(Guid.NewGuid());
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns(ConnectionId);
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        var hub = new TransferHub(_db) { Context = context.Object, Groups = _groups.Object };

        await hub.JoinTransferGroup(transferId);

        _groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeaveTransferGroup_RemovesTheConnectionFromThatTransfersGroup()
    {
        var transferId = Guid.NewGuid();

        await HubFor(Guid.NewGuid()).LeaveTransferGroup(transferId);

        _groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, transferId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // What the browser receives: the group and the message name are the client contract
    // =========================================================================

    private static (Mock<IHubContext<TransferHub>> Hub, Mock<IClientProxy> Proxy, Mock<IHubClients> Clients) HubContext()
    {
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        clients.SetupGet(c => c.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<TransferHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        return (hub, proxy, clients);
    }

    private static readonly TransferProgressUpdate Update = new(Guid.NewGuid(), TransferStatus.UploadingToYoto, 40, "Uploading", null);

    [Fact]
    public async Task SendProgressAsync_SendsTransferProgressToThatTransfersGroupOnly()
    {
        var (hub, proxy, clients) = HubContext();

        await new SignalRTransferProgressNotifier(hub.Object).SendProgressAsync(Update, CancellationToken.None);

        clients.Verify(c => c.Group(Update.TransferId.ToString()), Times.Once);
        proxy.Verify(p => p.SendCoreAsync("TransferProgress", It.Is<object?[]>(a => a.Single() == (object)Update), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyListChangedAsync_TellsEveryClientTheListChanged()
    {
        var (hub, proxy, _) = HubContext();

        await new SignalRTransferProgressNotifier(hub.Object).NotifyListChangedAsync();

        proxy.Verify(p => p.SendCoreAsync("TransferListChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendProgressUpdateAsync_SendsTransferProgressToThatTransfersGroup()
    {
        var (hub, proxy, clients) = HubContext();

        await hub.Object.SendProgressUpdateAsync(Update);

        clients.Verify(c => c.Group(Update.TransferId.ToString()), Times.Once);
        proxy.Verify(p => p.SendCoreAsync("TransferProgress", It.Is<object?[]>(a => a.Single() == (object)Update), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Card sets: only your own set's progress, on a group that can't clash with a transfer's
    // =========================================================================

    private async Task<Guid> AddCardSetOwnedByAsync(Guid ownerId)
    {
        var set = new CardSet { UserConnectionId = ownerId, AbsSeriesId = "ser-1", AbsLibraryId = "lib-1", Name = "Dragons" };
        _db.CardSets.Add(set);
        await _db.SaveChangesAsync();
        return set.Id;
    }

    [Fact]
    public async Task JoinCardSetGroup_ForOwnSet_JoinsThatSetsGroup()
    {
        var caller = Guid.NewGuid();
        var setId = await AddCardSetOwnedByAsync(caller);

        await HubFor(caller).JoinCardSetGroup(setId);

        _groups.Verify(g => g.AddToGroupAsync(ConnectionId, $"cardset:{setId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinCardSetGroup_ForAnotherUsersSet_DoesNotJoin()
    {
        var setId = await AddCardSetOwnedByAsync(Guid.NewGuid());

        await HubFor(Guid.NewGuid()).JoinCardSetGroup(setId);

        _groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeaveCardSetGroup_LeavesThatSetsGroup()
    {
        var setId = Guid.NewGuid();

        await HubFor(Guid.NewGuid()).LeaveCardSetGroup(setId);

        _groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, $"cardset:{setId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CardChangedAsync_SendsCardSetProgressToThatSetsGroupOnly()
    {
        var (hub, proxy, clients) = HubContext();
        var update = new CardSetProgressUpdate(Guid.NewGuid(), Guid.NewGuid(), 2, PlaylistStatus.Transferred, null);

        await new SignalRCardSetProgressNotifier(hub.Object).CardChangedAsync(update);

        clients.Verify(c => c.Group($"cardset:{update.CardSetId}"), Times.Once);
        proxy.Verify(p => p.SendCoreAsync("CardSetProgress", It.Is<object?[]>(a => a.Single() == (object)update), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendsChangedAsync_TellsEveryClientTheTransferListChanged()
    {
        var (hub, proxy, _) = HubContext();

        await new SignalRCardSetProgressNotifier(hub.Object).SendsChangedAsync();

        proxy.Verify(p => p.SendCoreAsync("TransferListChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
