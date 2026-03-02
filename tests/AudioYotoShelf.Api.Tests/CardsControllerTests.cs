using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class CardsControllerTests : IDisposable
{
    private readonly AudioYotoShelfDbContext _db;
    private readonly Mock<IYotoService> _yotoService;
    private readonly CardsController _sut;

    public CardsControllerTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"CardsCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);

        _yotoService = new Mock<IYotoService>();

        _sut = new CardsController(
            _yotoService.Object, _db,
            Mock.Of<ILogger<CardsController>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task GetCards_ReturnsCardsFromYotoService()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _yotoService.Setup(s => s.GetUserCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new YotoCard("card-1", null, new YotoCardMetadata("Author", "stories", "My Book", null, null, 3, 8, null, null)),
                new YotoCard("card-2", null, null)
            ]);

        var result = await _sut.GetCards(user.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCards_EnrichesWithTransferHistory()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);

        var transfer = TestData.CreateCardTransfer(user.Id, "Test Book");
        transfer.YotoCardId = "card-1";
        _db.CardTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        _yotoService.Setup(s => s.GetUserCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new YotoCard("card-1", null, null),
                new YotoCard("card-2", null, null)
            ]);

        var result = await _sut.GetCards(user.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetCards_NoYotoConnection_Returns401()
    {
        var user = TestData.CreateUserConnection(yotoAccessToken: null, yotoRefreshToken: null)
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        var result = await _sut.GetCards(user.Id, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetCards_UserNotFound_Returns401()
    {
        var result = await _sut.GetCards(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetCard_ReturnsCardDetail()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        var card = new YotoCard("card-1",
            new YotoCardContent([
                new YotoChapter("01", "Chapter 1", [
                    new YotoTrack("t1", "Track 1", "yoto:#abc", "mp3", "audio", 120, 1024, "stereo", null)
                ], null)
            ], null, "linear", 1),
            null);

        _yotoService.Setup(s => s.GetCardContentAsync(It.IsAny<string>(), "card-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var result = await _sut.GetCard(user.Id, "card-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DeleteCard_CallsYotoService()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _yotoService.Setup(s => s.DeleteCardAsync(It.IsAny<string>(), "card-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.DeleteCard(user.Id, "card-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _yotoService.Verify(s => s.DeleteCardAsync(user.YotoAccessToken!, "card-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCard_NoYotoConnection_Returns401()
    {
        var user = TestData.CreateUserConnection(yotoAccessToken: null, yotoRefreshToken: null);
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteCard(user.Id, "card-1", CancellationToken.None);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
