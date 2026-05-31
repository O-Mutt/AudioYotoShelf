using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class LibrariesControllerTests : IDisposable
{
    private readonly AudioYotoShelfDbContext _db;
    private readonly Mock<IAudiobookshelfService> _absService;
    private readonly Mock<IAgeSuggestionService> _ageService;
    private readonly LibrariesController _sut;

    public LibrariesControllerTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"LibCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);

        _absService = new Mock<IAudiobookshelfService>();
        _ageService = new Mock<IAgeSuggestionService>();

        _sut = new LibrariesController(
            _absService.Object, _ageService.Object, _db);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // =========================================================================
    // GetLibraryItems — search passthrough
    // =========================================================================

    [Fact]
    public async Task GetItems_WithSort_PassesSortToService()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetLibraryItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbsLibraryItemsResponse([], 0, 20, 0));

        await _sut.GetLibraryItems(user.Id, "lib-1", sort: "media.duration");

        _absService.Verify(s => s.GetLibraryItemsAsync(
            It.IsAny<string>(), It.IsAny<string>(), "lib-1",
            0, 20, "media.duration", false, false,
            null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetItems_NoSort_DefaultsToTitle()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetLibraryItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbsLibraryItemsResponse([], 0, 20, 0));

        await _sut.GetLibraryItems(user.Id, "lib-1");

        _absService.Verify(s => s.GetLibraryItemsAsync(
            It.IsAny<string>(), It.IsAny<string>(), "lib-1",
            0, 20, "media.metadata.title", false, false,
            null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetItems_WithPagination_PassesPageAndLimit()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetLibraryItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbsLibraryItemsResponse([], 100, 10, 3));

        var result = await _sut.GetLibraryItems(user.Id, "lib-1", page: 3, limit: 10);

        result.Should().BeOfType<OkObjectResult>();
        _absService.Verify(s => s.GetLibraryItemsAsync(
            It.IsAny<string>(), It.IsAny<string>(), "lib-1",
            3, 10, It.IsAny<string?>(), false, false,
            null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Auth checks
    // =========================================================================

    [Fact]
    public async Task GetItems_NoAuth_Returns401()
    {
        var result = await _sut.GetLibraryItems(Guid.NewGuid(), "lib-1");
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetItems_ExpiredAbsToken_Returns401()
    {
        var user = TestData.CreateUserConnection();
        user.AudiobookshelfToken = null; // Invalidate
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        var result = await _sut.GetLibraryItems(user.Id, "lib-1");
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // =========================================================================
    // GetLibraries
    // =========================================================================

    [Fact]
    public async Task GetLibraries_FiltersToBookType()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetLibrariesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AbsLibrary("lib-1", "My Books", "book", null),
                new AbsLibrary("lib-2", "Podcasts", "podcast", null),
                new AbsLibrary("lib-3", "Kids Books", "book", null),
            ]);

        var result = await _sut.GetLibraries(user.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var libraries = ok.Value as AbsLibrary[];
        libraries.Should().HaveCount(2);
        libraries.Should().OnlyContain(l => l.MediaType == "book");
    }

    // =========================================================================
    // GetItem — includes age suggestion
    // =========================================================================

    [Fact]
    public async Task GetItem_ReturnsAgeSuggestion()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), "item-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsLibraryItem("item-1"));

        _ageService.Setup(s => s.SuggestAgeRange(
                It.IsAny<AbsBookMetadata>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns(new Core.DTOs.Transfer.AgeSuggestionResponse(
                5, 10, "Genre-based", Core.Enums.AgeRangeSource.GenreInferred, []));

        var result = await _sut.GetItem(user.Id, "item-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // =========================================================================
    // Search routes to the dedicated ABS search endpoint
    // =========================================================================

    [Fact]
    public async Task GetItems_WithSearch_UsesSearchEndpointNotItems()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.SearchLibraryItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), "lib-1", "narnia", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestData.CreateAbsLibraryItem("book-1")]);

        var result = await _sut.GetLibraryItems(user.Id, "lib-1", search: "narnia");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        (ok.Value as AbsLibraryItemsResponse)!.Total.Should().Be(1);

        _absService.Verify(s => s.SearchLibraryItemsAsync(
            It.IsAny<string>(), It.IsAny<string>(), "lib-1", "narnia", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _absService.Verify(s => s.GetLibraryItemsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // Multi-library series resolution
    // =========================================================================

    [Fact]
    public async Task GetSeriesDetail_WithLibraryId_UsesItDirectly()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), "lib-2", "s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsSeriesItem("Harry Potter", 3));

        var result = await _sut.GetSeriesDetail(user.Id, "s1", "lib-2", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _absService.Verify(s => s.GetLibrariesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSeriesDetail_NoLibraryId_SearchesBookLibrariesUntilBooksFound()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        _absService.Setup(s => s.GetLibrariesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AbsLibrary("lib-a", "A", "book", null), new AbsLibrary("lib-b", "B", "book", null)]);

        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), "lib-a", "s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbsSeriesItem("s1", "Harry Potter", null, [], 0)); // not here
        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), "lib-b", "s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsSeriesItem("Harry Potter", 3));      // lives here

        var result = await _sut.GetSeriesDetail(user.Id, "s1", null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        (ok.Value as AbsSeriesItem)!.Books.Should().HaveCount(3);
    }
}
