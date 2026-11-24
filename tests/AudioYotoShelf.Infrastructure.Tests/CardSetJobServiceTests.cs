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
    private readonly CardSetJobService _sut;

    public CardSetJobServiceTests() =>
        _sut = new CardSetJobService(_orchestrator.Object, _fixture.DbContext, Mock.Of<ILogger<CardSetJobService>>());

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
}
