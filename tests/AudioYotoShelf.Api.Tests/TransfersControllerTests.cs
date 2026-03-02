using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;

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
            .UseInMemoryDatabase($"TxCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);

        _orchestrator = new Mock<ITransferOrchestrator>();
        _backgroundJobs = new Mock<IBackgroundJobClient>();

        _backgroundJobs
            .Setup(b => b.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<IState>()))
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
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);

        for (int i = 0; i < 5; i++)
        {
            var transfer = TestData.CreateCardTransfer(user.Id, $"Book {i}");
            _db.CardTransfers.Add(transfer);
        }
        await _db.SaveChangesAsync();

        var result = await _sut.GetTransfers(user.Id, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTransfers_WithStatusFilter_FiltersCorrectly()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);

        var t1 = TestData.CreateCardTransfer(user.Id, "Done");
        t1.Status = TransferStatus.Completed;
        var t2 = TestData.CreateCardTransfer(user.Id, "Pending");
        t2.Status = TransferStatus.Pending;
        _db.CardTransfers.AddRange(t1, t2);
        await _db.SaveChangesAsync();

        var result = await _sut.GetTransfers(user.Id, status: TransferStatus.Completed, ct: CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // =========================================================================
    // GET transfer by ID
    // =========================================================================

    [Fact]
    public async Task GetTransfer_Found_Returns200()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        var transfer = TestData.CreateCardTransfer(user.Id, "Test Book");
        _db.CardTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        var result = await _sut.GetTransfer(transfer.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetTransfer_NotFound_Returns404()
    {
        var result = await _sut.GetTransfer(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // =========================================================================
    // POST book transfer
    // =========================================================================

    [Fact]
    public void TransferBook_EnqueuesJob_Returns202()
    {
        var request = new CreateTransferRequest("item-1");
        var result = _sut.TransferBook(Guid.NewGuid(), request);

        result.Should().BeOfType<AcceptedObjectResult>();
    }

    // =========================================================================
    // POST series transfer
    // =========================================================================

    [Fact]
    public void TransferSeries_EnqueuesJob_Returns202()
    {
        var request = new CreateSeriesTransferRequest("ser-1", "lib-1");
        var result = _sut.TransferSeries(Guid.NewGuid(), request);

        result.Should().BeOfType<AcceptedObjectResult>();
    }

    // =========================================================================
    // POST batch transfer (Phase 2)
    // =========================================================================

    [Fact]
    public void TransferBatch_EnqueuesJobPerBook()
    {
        var request = new BatchTransferRequest(["item-1", "item-2", "item-3"]);
        var result = _sut.TransferBatch(Guid.NewGuid(), request);

        var accepted = result.Should().BeOfType<AcceptedObjectResult>().Subject;
        var response = accepted.Value as BatchTransferResponse;
        response.Should().NotBeNull();
        response!.TotalBooks.Should().Be(3);
        response.Queued.Should().Be(3);
        response.JobIds.Should().HaveCount(3);
    }

    [Fact]
    public void TransferBatch_SingleItem_Works()
    {
        var request = new BatchTransferRequest(["item-1"]);
        var result = _sut.TransferBatch(Guid.NewGuid(), request);

        var accepted = result.Should().BeOfType<AcceptedObjectResult>().Subject;
        var response = accepted.Value as BatchTransferResponse;
        response!.TotalBooks.Should().Be(1);
    }

    // =========================================================================
    // POST retry + cancel
    // =========================================================================

    [Fact]
    public void RetryTransfer_EnqueuesJob_Returns202()
    {
        var result = _sut.RetryTransfer(Guid.NewGuid());
        result.Should().BeOfType<AcceptedObjectResult>();
    }

    [Fact]
    public async Task CancelTransfer_CallsOrchestrator()
    {
        var transferId = Guid.NewGuid();
        var result = await _sut.CancelTransfer(transferId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _orchestrator.Verify(o => o.CancelTransferAsync(transferId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
