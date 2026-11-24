using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using AudioYotoShelf.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class CardSetJobServiceTests : IDisposable
{
    private readonly InMemoryDbFixture _fixture = new();
    private readonly Mock<IPlaylistTransferOrchestrator> _orchestrator = new();
    private readonly Mock<ICardSetProgressNotifier> _notifier = new();
    private readonly List<CardSetProgressUpdate> _pushed = [];
    private readonly CardSetJobService _sut;

    public CardSetJobServiceTests()
    {
        _notifier.Setup(n => n.CardChangedAsync(It.IsAny<CardSetProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<CardSetProgressUpdate, CancellationToken>((u, _) => _pushed.Add(u))
            .Returns(Task.CompletedTask);
        _sut = new CardSetJobService(
            _orchestrator.Object, _fixture.DbContext, _notifier.Object, Mock.Of<ILogger<CardSetJobService>>());
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<Guid[]> SeedCardsAsync(params PlaylistStatus[] statuses)
    {
        var cards = statuses.Select((s, i) => new Playlist
        {
            UserConnectionId = Guid.NewGuid(),
            Name = $"Card {i + 1}",
            Status = s,
        }).ToList();
        _fixture.DbContext.Playlists.AddRange(cards);
        await _fixture.DbContext.SaveChangesAsync();
        return cards.Select(c => c.Id).ToArray();
    }

    private async Task<PlaylistStatus> StatusAsync(Guid id) =>
        (await _fixture.DbContext.Playlists.AsNoTracking().FirstAsync(p => p.Id == id)).Status;

    [Fact]
    public async Task SendsTheCardsInOrder()
    {
        var ids = await SeedCardsAsync(PlaylistStatus.Queued, PlaylistStatus.Queued, PlaylistStatus.Queued);
        var sent = new List<Guid>();
        _orchestrator.Setup(o => o.TransferPlaylistAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => sent.Add(id))
            .Returns(Task.CompletedTask);

        await _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        sent.Should().Equal(ids);
    }

    [Fact]
    public async Task AFailedCard_DoesNotStopTheRest()
    {
        var ids = await SeedCardsAsync(PlaylistStatus.Queued, PlaylistStatus.Queued, PlaylistStatus.Queued);
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[1], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Yoto said no"));

        var act = () => _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[2], It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACardThatFailsBeforeSending_IsMarkedFailedNotLeftQueued()
    {
        var ids = await SeedCardsAsync(PlaylistStatus.Queued);
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[0], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No valid Yoto connection"));

        await _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        var card = await _fixture.DbContext.Playlists.AsNoTracking().FirstAsync(p => p.Id == ids[0]);
        card.Status.Should().Be(PlaylistStatus.Failed);
        card.ErrorMessage.Should().Be("No valid Yoto connection");
    }

    [Fact]
    public async Task ARerunAfterARestart_SkipsCardsAlreadyOnYoto()
    {
        var ids = await SeedCardsAsync(PlaylistStatus.Transferred, PlaylistStatus.Queued);

        await _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[0], It.IsAny<CancellationToken>()), Times.Never);
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[1], It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_StopsAndPutsTheUnsentCardsBackToDraft()
    {
        var ids = await SeedCardsAsync(PlaylistStatus.Queued, PlaylistStatus.Queued);
        using var cts = new CancellationTokenSource();
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[0], It.IsAny<CancellationToken>()))
            .Callback(cts.Cancel)
            .Returns(Task.CompletedTask);

        var act = () => _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[1], It.IsAny<CancellationToken>()), Times.Never);
        (await StatusAsync(ids[1])).Should().Be(PlaylistStatus.Draft);
    }

    [Fact]
    public async Task PushesEachCardsProgressToTheSet()
    {
        var setId = Guid.NewGuid();
        var ids = await SeedCardsAsync(PlaylistStatus.Queued, PlaylistStatus.Queued);
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[0], It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var card = await _fixture.DbContext.Playlists.FirstAsync(p => p.Id == ids[0]);
                card.Status = PlaylistStatus.Transferred;
                await _fixture.DbContext.SaveChangesAsync();
            });
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[1], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No valid Yoto connection"));

        await _sut.ExecuteCardSetTransferAsync(setId, ids, CancellationToken.None);

        _pushed.Should().OnlyContain(u => u.CardSetId == setId);
        _pushed.Select(u => (u.PlaylistId, u.Status)).Should().Equal(
            (ids[0], PlaylistStatus.Transferring),
            (ids[0], PlaylistStatus.Transferred),
            (ids[1], PlaylistStatus.Transferring),
            (ids[1], PlaylistStatus.Failed));
        _pushed.Last().ErrorMessage.Should().Be("No valid Yoto connection");
    }

    [Fact]
    public async Task APushThatFails_DoesNotStopTheSend()
    {
        var ids = await SeedCardsAsync(PlaylistStatus.Queued);
        _notifier.Setup(n => n.CardChangedAsync(It.IsAny<CardSetProgressUpdate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        await _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[0], It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task ARerun_SkipsCardsItNoLongerOwns()
    {
        // Back to draft after a cancel, failed, or already on Yoto: none of them is this job's to send.
        var ids = await SeedCardsAsync(
            PlaylistStatus.Draft, PlaylistStatus.Failed, PlaylistStatus.Transferred,
            PlaylistStatus.Transferring, PlaylistStatus.Queued);

        await _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        _orchestrator.Verify(o => o.TransferPlaylistAsync(It.IsIn(ids[0], ids[1], ids[2]), It.IsAny<CancellationToken>()), Times.Never);
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[3], It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[4], It.IsAny<CancellationToken>()), Times.Once);
    }
}
