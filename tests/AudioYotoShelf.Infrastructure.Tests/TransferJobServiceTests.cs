using AudioYotoShelf.Api.Hubs;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class TransferJobServiceTests
{
    private readonly Mock<ITransferOrchestrator> _orchestrator;
    private readonly Mock<IHubContext<TransferHub>> _hubContext;
    private readonly TransferJobService _sut;

    public TransferJobServiceTests()
    {
        _orchestrator = new Mock<ITransferOrchestrator>();
        _hubContext = new Mock<IHubContext<TransferHub>>();

        // Setup hub to accept any SendAsync call
        var mockClients = new Mock<IHubClients>();
        var mockProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        _sut = new TransferJobService(
            _orchestrator.Object, _hubContext.Object,
            Mock.Of<ILogger<TransferJobService>>());
    }

    // =========================================================================
    // ExecuteBookTransferAsync
    // =========================================================================

    [Fact]
    public async Task ExecuteBookTransferAsync_CallsOrchestrator()
    {
        var userId = Guid.NewGuid();
        var request = TestData.CreateTransferRequest();
        var response = new TransferResponse(
            Guid.NewGuid(), "Test Book", "Author", null, null,
            TransferStatus.Completed, 100, null,
            new AgeRangeResponse(5, 10, "Test", AgeRangeSource.Default, null, null, 5, 10),
            "card-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        _orchestrator.Setup(o => o.TransferBookAsync(userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        await _sut.ExecuteBookTransferAsync(userId, request, CancellationToken.None);

        _orchestrator.Verify(o => o.TransferBookAsync(userId, request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBookTransferAsync_SendsSignalRProgressOnComplete()
    {
        var userId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var response = new TransferResponse(
            transferId, "Test Book", "Author", null, null,
            TransferStatus.Completed, 100, null,
            new AgeRangeResponse(5, 10, "Test", AgeRangeSource.Default, null, null, 5, 10),
            "card-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        _orchestrator.Setup(o => o.TransferBookAsync(
                It.IsAny<Guid>(), It.IsAny<CreateTransferRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        await _sut.ExecuteBookTransferAsync(userId, TestData.CreateTransferRequest(), CancellationToken.None);

        _hubContext.Verify(h => h.Clients, Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteBookTransferAsync_OrchestratorThrows_PropagatesException()
    {
        _orchestrator.Setup(o => o.TransferBookAsync(
                It.IsAny<Guid>(), It.IsAny<CreateTransferRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("External failure"));

        var act = () => _sut.ExecuteBookTransferAsync(
            Guid.NewGuid(), TestData.CreateTransferRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // =========================================================================
    // ExecuteSeriesTransferAsync
    // =========================================================================

    [Fact]
    public async Task ExecuteSeriesTransferAsync_CallsOrchestrator()
    {
        var userId = Guid.NewGuid();
        var request = TestData.CreateSeriesTransferRequest();

        _orchestrator.Setup(o => o.TransferSeriesAsync(
                userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.ExecuteSeriesTransferAsync(userId, request, CancellationToken.None);

        _orchestrator.Verify(o => o.TransferSeriesAsync(userId, request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // ExecuteRetryTransferAsync
    // =========================================================================

    [Fact]
    public async Task ExecuteRetryTransferAsync_CallsOrchestrator()
    {
        var transferId = Guid.NewGuid();
        var response = new TransferResponse(
            transferId, "Test Book", "Author", null, null,
            TransferStatus.Completed, 100, null,
            new AgeRangeResponse(5, 10, "Test", AgeRangeSource.Default, null, null, 5, 10),
            "card-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        _orchestrator.Setup(o => o.RetryTransferAsync(transferId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        await _sut.ExecuteRetryTransferAsync(transferId, CancellationToken.None);

        _orchestrator.Verify(o => o.RetryTransferAsync(transferId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
