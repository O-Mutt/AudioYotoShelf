using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class TransfersControllerTests : IDisposable
{
    private readonly AudioYotoShelfDbContext _db;
    private readonly Mock<ITransferOrchestrator> _orchestrator;
    private readonly Mock<IBackgroundJobClient> _backgroundJobs;
    private readonly TransfersController _sut;

    public TransfersControllerTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"TransfersCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);

        _orchestrator = new Mock<ITransferOrchestrator>();
        _backgroundJobs = new Mock<IBackgroundJobClient>();

        _backgroundJobs.Setup(b => b.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns("job-123");

        _sut = new TransfersController(
            _db, _orchestrator.Object, _backgroundJobs.Object,
            Mock.Of<ILogger<TransfersController>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // =========================================================================
    // GET transfers
    // =========================================================================

    [Fact]
    public async Task GetTransfers_ReturnsPaginatedList()
    {
        var userId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
        {
            _db.CardTransfers.Add(TestData.CreateCardTransfer(
                userConnectionId: userId, title: $"Book {i}"));
        }
        await _db.SaveChangesAsync();

        var result = await _sut.GetTransfers(userId, page: 0, limit: 3);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value;
        value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTransfers_WithStatusFilter_FiltersResults()
    {
        var userId = Guid.NewGuid();
        _db.CardTransfers.Add(TestData.CreateCardTransfer(
            userConnectionId: userId, status: TransferStatus.Completed));
        _db.CardTransfers.Add(TestData.CreateCardTransfer(
            userConnectionId: userId, status: TransferStatus.Failed));
        await _db.SaveChangesAsync();

        var result = await _sut.GetTransfers(userId, status: TransferStatus.Completed);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetTransfer_ExistingId_ReturnsOk()
    {
        var transfer = TestData.CreateCardTransfer();
        _db.CardTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        var result = await _sut.GetTransfer(transfer.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetTransfer_NonExistentId_ReturnsNotFound()
    {
        var result = await _sut.GetTransfer(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // =========================================================================
    // POST book transfer
    // =========================================================================

    [Fact]
    public void TransferBook_EnqueuesHangfireJob()
    {
        var request = TestData.CreateTransferRequest("item-abc");
        var result = _sut.TransferBook(Guid.NewGuid(), request);

        result.Should().BeOfType<AcceptedResult>();
        _backgroundJobs.Verify(b => b.Create(
            It.IsAny<Job>(),
            It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public void TransferBook_ReturnsJobId()
    {
        var result = _sut.TransferBook(Guid.NewGuid(), TestData.CreateTransferRequest());

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().NotBeNull();
    }

    // =========================================================================
    // POST series transfer
    // =========================================================================

    [Fact]
    public void TransferSeries_EnqueuesHangfireJob()
    {
        var request = TestData.CreateSeriesTransferRequest();
        var result = _sut.TransferSeries(Guid.NewGuid(), request);

        result.Should().BeOfType<AcceptedResult>();
        _backgroundJobs.Verify(b => b.Create(
            It.IsAny<Job>(),
            It.IsAny<IState>()),
            Times.Once);
    }

    // =========================================================================
    // POST retry
    // =========================================================================

    [Fact]
    public void RetryTransfer_EnqueuesRetryJob()
    {
        var result = _sut.RetryTransfer(Guid.NewGuid());

        result.Should().BeOfType<AcceptedResult>();
        _backgroundJobs.Verify(b => b.Create(
            It.IsAny<Job>(),
            It.IsAny<IState>()),
            Times.Once);
    }

    // =========================================================================
    // POST cancel
    // =========================================================================

    [Fact]
    public async Task CancelTransfer_CallsOrchestrator()
    {
        var transferId = Guid.NewGuid();
        var result = await _sut.CancelTransfer(transferId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _orchestrator.Verify(o => o.CancelTransferAsync(transferId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
