using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Services;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Services;
using AudioYotoShelf.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class PlaylistServiceTests : IDisposable
{
    private readonly InMemoryDbFixture _fixture;
    private readonly Mock<IAudiobookshelfService> _absService;
    private readonly PlaylistService _sut;

    public PlaylistServiceTests()
    {
        _fixture = new InMemoryDbFixture();
        _absService = new Mock<IAudiobookshelfService>();
        var limits = new YotoCardLimits();
        _sut = new PlaylistService(
            _fixture.DbContext, _absService.Object,
            new TrackPlanner(limits), new CardCapacityCalculator(limits),
            Mock.Of<ILogger<PlaylistService>>());
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<Guid> SeedUserAsync()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);
        await _fixture.DbContext.SaveChangesAsync();
        return user.Id;
    }

    private void SetupBook(string itemId, AbsBookMedia media) =>
        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsLibraryItem(itemId, media));

    [Fact]
    public async Task Create_DefaultsToDraftAndAutoGrouping()
    {
        var userId = await SeedUserAsync();

        var result = await _sut.CreateAsync(userId, new CreatePlaylistRequest("My Mix"));

        result.Name.Should().Be("My Mix");
        result.Status.Should().Be(PlaylistStatus.Draft);
        result.DefaultGrouping.Should().Be(TrackGrouping.Auto);
        result.Capacity.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public async Task AddItems_CachesTracksAndComputesCapacity()
    {
        var userId = await SeedUserAsync();
        var playlist = await _sut.CreateAsync(userId, new CreatePlaylistRequest("Mix", TrackGrouping.Chapters));

        SetupBook("book-1", TestData.CreateAbsMedia(audioFiles:
        [
            TestData.CreateAbsAudioFile(0, "ino-1", duration: 600, size: 10_000_000),
            TestData.CreateAbsAudioFile(1, "ino-2", duration: 600, size: 10_000_000),
        ]));

        var result = await _sut.AddItemsAsync(playlist.Id, ["book-1"]);

        result.Items.Should().HaveCount(1);
        result.Items[0].ProjectedTrackCount.Should().Be(2);
        result.Capacity.Tracks.Should().Be(2);
        result.Capacity.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public async Task AddItems_DuplicateBook_IsIgnored()
    {
        var userId = await SeedUserAsync();
        var playlist = await _sut.CreateAsync(userId, new CreatePlaylistRequest("Mix"));
        SetupBook("book-1", TestData.CreateAbsMedia());

        await _sut.AddItemsAsync(playlist.Id, ["book-1"]);
        var result = await _sut.AddItemsAsync(playlist.Id, ["book-1"]);

        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Capacity_FlagsOverflowAndFirstBook_WhenTooLong()
    {
        var userId = await SeedUserAsync();
        var playlist = await _sut.CreateAsync(userId, new CreatePlaylistRequest("Mix", TrackGrouping.Chapters));

        // One 6-hour book (single track) exceeds the 5h total limit.
        SetupBook("big-book", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 21600, size: 50_000_000)],
            chapters: []));

        var result = await _sut.AddItemsAsync(playlist.Id, ["big-book"]);

        result.Capacity.WithinLimits.Should().BeFalse();
        result.Capacity.ExceedsDuration.Should().BeTrue();
        result.Capacity.FirstOverflowItemId.Should().Be("big-book");
    }

    [Fact]
    public async Task Reorder_UpdatesPositions()
    {
        var userId = await SeedUserAsync();
        var playlist = await _sut.CreateAsync(userId, new CreatePlaylistRequest("Mix"));
        SetupBook("a", TestData.CreateAbsMedia());
        SetupBook("b", TestData.CreateAbsMedia());
        var withItems = await _sut.AddItemsAsync(playlist.Id, ["a", "b"]);
        var idA = withItems.Items.Single(i => i.AbsLibraryItemId == "a").Id;
        var idB = withItems.Items.Single(i => i.AbsLibraryItemId == "b").Id;

        var result = await _sut.ReorderAsync(playlist.Id, [idB, idA]);

        result.Items.Select(i => i.AbsLibraryItemId).Should().Equal("b", "a");
    }

    [Fact]
    public async Task SetItemGrouping_OverridesProjection()
    {
        var userId = await SeedUserAsync();
        var playlist = await _sut.CreateAsync(userId, new CreatePlaylistRequest("Mix", TrackGrouping.Chapters));
        SetupBook("book-1", TestData.CreateAbsMedia(audioFiles:
        [
            TestData.CreateAbsAudioFile(0, "ino-1", duration: 300, size: 5_000_000),
            TestData.CreateAbsAudioFile(1, "ino-2", duration: 300, size: 5_000_000),
        ]));
        var added = await _sut.AddItemsAsync(playlist.Id, ["book-1"]);
        added.Items[0].ProjectedTrackCount.Should().Be(2);

        var result = await _sut.SetItemGroupingAsync(playlist.Id, added.Items[0].Id, TrackGrouping.SingleTrack);

        result.Items[0].EffectiveGrouping.Should().Be(TrackGrouping.SingleTrack);
        result.Items[0].ProjectedTrackCount.Should().Be(1); // merged into one track (within 1h)
    }
}
