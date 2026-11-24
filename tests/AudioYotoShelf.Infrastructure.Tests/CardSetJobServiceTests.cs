using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class CardSetJobServiceTests
{
    private readonly Mock<IPlaylistTransferOrchestrator> _orchestrator = new();
    private readonly CardSetJobService _sut;

    public CardSetJobServiceTests() =>
        _sut = new CardSetJobService(_orchestrator.Object, Mock.Of<ILogger<CardSetJobService>>());

    [Fact]
    public async Task SendsTheCardsInOrder()
    {
        var sent = new List<Guid>();
        _orchestrator.Setup(o => o.TransferPlaylistAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => sent.Add(id))
            .Returns(Task.CompletedTask);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        sent.Should().Equal(ids);
    }

    [Fact]
    public async Task AFailedCard_DoesNotStopTheRest()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[1], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Yoto said no"));

        var act = () => _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[2], It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_StopsBeforeTheNextCard()
    {
        using var cts = new CancellationTokenSource();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _orchestrator.Setup(o => o.TransferPlaylistAsync(ids[0], It.IsAny<CancellationToken>()))
            .Callback(cts.Cancel)
            .Returns(Task.CompletedTask);

        var act = () => _sut.ExecuteCardSetTransferAsync(Guid.NewGuid(), ids, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _orchestrator.Verify(o => o.TransferPlaylistAsync(ids[1], It.IsAny<CancellationToken>()), Times.Never);
    }
}
