using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Admin;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Tests;

public class AdminControllerTests : IDisposable
{
    private readonly AudioYotoShelfDbContext _db;
    private readonly AdminController _sut;

    public AdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"AdminCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);
        _sut = new AdminController(_db);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task Overview_AggregatesUsersLoginsAndTransfers()
    {
        var alice = TestData.CreateUserConnection(username: "alice");
        alice.IsAdmin = true;
        alice.LastLoginAt = DateTimeOffset.UtcNow;
        var bob = TestData.CreateUserConnection(username: "bob");
        _db.UserConnections.AddRange(alice, bob);

        _db.LoginEvents.AddRange(
            new LoginEvent { UserConnectionId = alice.Id },
            new LoginEvent { UserConnectionId = alice.Id },
            new LoginEvent { UserConnectionId = bob.Id });

        var completed = TestData.CreateCardTransfer(alice.Id, "item-a");
        completed.Status = TransferStatus.Completed;
        var failed = TestData.CreateCardTransfer(alice.Id, "item-b");
        failed.Status = TransferStatus.Failed;
        _db.CardTransfers.AddRange(completed, failed);
        await _db.SaveChangesAsync();

        var result = await _sut.Overview(CancellationToken.None);

        var overview = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<AdminOverview>().Subject;
        overview.TotalUsers.Should().Be(2);
        overview.AdminUsers.Should().Be(1);
        overview.ActiveUsers7d.Should().Be(1);
        overview.TotalLogins.Should().Be(3);
        overview.TotalTransfers.Should().Be(2);
        overview.CompletedTransfers.Should().Be(1);
        overview.FailedTransfers.Should().Be(1);
        overview.TransferSuccessRate.Should().Be(50);
    }

    [Fact]
    public async Task Users_ReturnsPerUserCounts()
    {
        var alice = TestData.CreateUserConnection(username: "alice");
        _db.UserConnections.Add(alice);
        _db.LoginEvents.Add(new LoginEvent { UserConnectionId = alice.Id });
        _db.CardTransfers.Add(TestData.CreateCardTransfer(alice.Id, "item-a"));
        await _db.SaveChangesAsync();

        var result = await _sut.Users(CancellationToken.None);

        var rows = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<AdminUserRow>>().Subject.ToList();
        rows.Should().HaveCount(1);
        rows[0].Username.Should().Be("alice");
        rows[0].LoginCount.Should().Be(1);
        rows[0].TransferCount.Should().Be(1);
    }

    [Fact]
    public async Task Usage_ReturnsRequestedNumberOfDays()
    {
        var result = await _sut.Usage(days: 7, CancellationToken.None);

        var points = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<UsagePoint>>().Subject.ToList();
        points.Should().HaveCount(7);
    }
}
