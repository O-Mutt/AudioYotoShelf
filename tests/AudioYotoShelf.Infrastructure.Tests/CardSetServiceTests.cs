using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Services;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Services;
using AudioYotoShelf.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class CardSetServiceTests : IDisposable
{
    private const string SeriesId = "series-1";
    private const string LibraryId = "lib-1";

    private readonly InMemoryDbFixture _fixture = new();
    private readonly Mock<IAudiobookshelfService> _absService = new();
    private readonly CardSetService _sut;

    public CardSetServiceTests()
    {
        // A 10-hour card keeps the arithmetic readable; the real default is about 11.6 h.
        var limits = new YotoCardLimits { MaxTotalBytes = 10 * 45_000_000L };
        var trackPlanner = new TrackPlanner(limits);
        var calculator = new CardCapacityCalculator(limits);
        _sut = new CardSetService(
            _fixture.DbContext, _absService.Object, trackPlanner, calculator,
            new CardSetPlanner(calculator, trackPlanner, limits), limits, Mock.Of<ILogger<CardSetService>>());
    }

    public void Dispose() => _fixture.Dispose();

    private record SeriesBookSpec(string Id, string? Sequence, params double[] ChapterHours);

    private async Task<Guid> SeedUserAsync()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);
        await _fixture.DbContext.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Serves a series from Audiobookshelf, in the order given. A book with several chapters is a
    /// multi-file book (one file per chapter); a book with one is a single file without chapters.
    /// </summary>
    private void SetupSeries(params SeriesBookSpec[] books)
    {
        var seriesBooks = books.Select(b => new AbsSeriesBook(b.Id, null, b.Sequence)).ToArray();
        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), LibraryId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbsSeriesItem(SeriesId, "Dragons", null, seriesBooks, 0));

        foreach (var book in books)
        {
            var files = book.ChapterHours
                .Select((h, i) => TestData.CreateAbsAudioFile(i, $"{book.Id}-ino-{i}", duration: h * 3600))
                .ToArray();
            var media = TestData.CreateAbsMedia(
                TestData.CreateAbsMetadata($"Title {book.Id}", seriesName: "Dragons", seriesSequence: book.Sequence),
                audioFiles: files, chapters: [], duration: book.ChapterHours.Sum() * 3600);
            _absService.Setup(s => s.GetLibraryItemAsync(
                    It.IsAny<string>(), It.IsAny<string>(), book.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestData.CreateAbsLibraryItem(book.Id, media));
        }
    }

    private static SeriesBookSpec[] HourBooks(int count) =>
        Enumerable.Range(1, count).Select(i => new SeriesBookSpec($"b{i}", i.ToString(), 1)).ToArray();

    private static CreateCardSetRequest Create(PackingMode mode = PackingMode.Balance, int[]? breaks = null,
        string[]? excluded = null, Dictionary<string, int[]>? splitPoints = null) =>
        new(SeriesId, LibraryId, mode, excluded, breaks, splitPoints);

    private async Task<List<Playlist>> CardsAsync(Guid cardSetId) =>
        await _fixture.DbContext.Playlists
            .Include(p => p.Items)
            .Where(p => p.CardSetId == cardSetId)
            .OrderBy(p => p.SetIndex)
            .ToListAsync();

    private async Task LockAsync(Guid playlistId)
    {
        var card = await _fixture.DbContext.Playlists.FirstAsync(p => p.Id == playlistId);
        card.Status = PlaylistStatus.Transferred;
        card.YotoCardId = "yoto-sent";
        await _fixture.DbContext.SaveChangesAsync();
    }

    private static string[] BookIds(Playlist card) =>
        card.Items.OrderBy(i => i.Position).Select(i => i.AbsLibraryItemId).ToArray();

    // =========================================================================
    // Plan
    // =========================================================================

    [Fact]
    public async Task Plan_OrdersBooksBySequence_FractionsInPlace_MissingLast()
    {
        var userId = await SeedUserAsync();
        SetupSeries(
            new SeriesBookSpec("b3", "3", 1), new SeriesBookSpec("none", null, 1),
            new SeriesBookSpec("b1", "1", 1), new SeriesBookSpec("b1.5", "1.5", 1), new SeriesBookSpec("b2", "2", 1));

        var plan = await _sut.PlanAsync(userId, new CardSetPlanRequest(SeriesId, LibraryId));

        plan.Books.Select(b => b.AbsLibraryItemId).Should().Equal("b1", "b1.5", "b2", "b3", "none");
    }

    [Fact]
    public async Task Plan_PacksTheSeriesAndTitlesTheCards()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));

        var plan = await _sut.PlanAsync(userId, new CardSetPlanRequest(SeriesId, LibraryId));

        plan.SeriesName.Should().Be("Dragons");
        plan.WithinLimits.Should().BeTrue();
        plan.Cards.Select(c => c.Title).Should().Equal("Dragons · Books 1–6", "Dragons · Books 7–12");
        plan.Cards.Should().OnlyContain(c => c.PlaylistId == null);
    }

    [Fact]
    public async Task Plan_ExcludedBooks_AreLeftOutAndListed()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));

        var plan = await _sut.PlanAsync(userId, new CardSetPlanRequest(SeriesId, LibraryId, ExcludedItemIds: ["b2"]));

        plan.Books.Select(b => b.AbsLibraryItemId).Should().Equal("b1", "b3");
        plan.ExcludedBooks.Should().ContainSingle(b => b.AbsLibraryItemId == "b2");
    }

    [Fact]
    public async Task Plan_ASplitBook_ListsItsChaptersAndParts()
    {
        var userId = await SeedUserAsync();
        SetupSeries(new SeriesBookSpec("big", "4", Enumerable.Repeat(1.0, 15).ToArray()));

        var plan = await _sut.PlanAsync(userId, new CardSetPlanRequest(SeriesId, LibraryId));

        plan.Books[0].Split.Should().BeTrue();
        plan.Books[0].Chapters.Should().HaveCount(15);
        plan.Cards.Select(c => c.Title).Should().Equal("Dragons 4 · Part 1 of 2", "Dragons 4 · Part 2 of 2");
    }

    [Fact]
    public async Task Plan_InvalidBreaks_AreABadRequest()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));

        var act = () => _sut.PlanAsync(userId, new CardSetPlanRequest(SeriesId, LibraryId, Breaks: [2, 1]));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // =========================================================================
    // Create
    // =========================================================================

    [Fact]
    public async Task Create_MakesOneDraftPlaylistPerCard()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));

        var set = await _sut.CreateAsync(userId, Create());

        var cards = await CardsAsync(set.Id);
        cards.Should().HaveCount(2);
        cards.Select(c => c.SetIndex).Should().Equal(0, 1);
        cards.Should().OnlyContain(c => c.Status == PlaylistStatus.Draft && c.DefaultGrouping == TrackGrouping.Auto);
        BookIds(cards[0]).Should().Equal("b1", "b2", "b3", "b4", "b5", "b6");
        cards[0].Name.Should().Be("Dragons · Books 1–6");
        cards[0].Items.First().SeriesSequence.Should().Be(1);
        set.Name.Should().Be("Dragons");
        set.Status.Should().Be(CardSetStatus.Draft);
    }

    [Fact]
    public async Task Create_UsesTheGivenNameAndCardTitles()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));

        var set = await _sut.CreateAsync(userId, Create() with { Name = "Bedtime", CardTitles = ["First", ""] });

        set.Name.Should().Be("Bedtime");
        set.Cards.Select(c => c.Title).Should().Equal("First", "Bedtime · Books 7–12");
    }

    [Fact]
    public async Task Create_ASecondSetForTheSameSeries_Conflicts()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var first = await _sut.CreateAsync(userId, Create());

        var act = () => _sut.CreateAsync(userId, Create());

        (await act.Should().ThrowAsync<CardSetConflictException>()).Which.CardSetId.Should().Be(first.Id);
    }

    [Fact]
    public async Task Create_WithBreaksThatOverflow_IsRefusedWithThePlan()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));

        var act = () => _sut.CreateAsync(userId, Create(breaks: [11]));

        var thrown = await act.Should().ThrowAsync<CardSetPlanException>();
        thrown.Which.Plan.WithinLimits.Should().BeFalse();
        (await _fixture.DbContext.CardSets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_ASplitBook_StoresEachPartsChapterRange()
    {
        var userId = await SeedUserAsync();
        SetupSeries(new SeriesBookSpec("big", "4", Enumerable.Repeat(1.0, 15).ToArray()));

        var set = await _sut.CreateAsync(userId, Create(splitPoints: new() { ["big"] = [8] }));

        var cards = await CardsAsync(set.Id);
        cards.Should().HaveCount(2);
        var parts = cards.Select(c => c.Items.Single()).ToList();
        parts.Should().OnlyContain(p => p.TrackDurations.Count == 15 && p.PartCount == 2);
        parts.Select(p => (p.FirstChapterIndex, p.LastChapterIndex, p.PartNumber))
            .Should().Equal((0, 7, 1), (8, 14, 2));
        set.Cards[1].FirstChapterIndex.Should().Be(8);
    }

    // =========================================================================
    // Change breaks
    // =========================================================================

    [Fact]
    public async Task UpdateBreaks_MovesBooksBetweenUnsentCards()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());

        var updated = await _sut.UpdateBreaksAsync(set.Id, new UpdateCardSetBreaksRequest([4]));

        updated.Cards.Select(c => (c.FirstBookIndex, c.LastBookIndex)).Should().Equal((0, 3), (4, 11));
        updated.Cards.Select(c => c.Title).Should().Equal("Dragons · Books 1–4", "Dragons · Books 5–12");
    }

    [Fact]
    public async Task UpdateBreaks_ThatOverflow_AreRefused()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());

        var act = () => _sut.UpdateBreaksAsync(set.Id, new UpdateCardSetBreaksRequest([11]));

        await act.Should().ThrowAsync<CardSetPlanException>();
    }

    [Fact]
    public async Task UpdateBreaks_ThatMoveASentCard_Conflict()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());
        await LockAsync(set.Cards[0].PlaylistId!.Value);

        var act = () => _sut.UpdateBreaksAsync(set.Id, new UpdateCardSetBreaksRequest([5]));

        await act.Should().ThrowAsync<CardSetConflictException>();
    }

    [Fact]
    public async Task UpdateBreaks_AfterASentCard_LeaveTheSentCardAlone()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(15));
        var set = await _sut.CreateAsync(userId, Create(breaks: [5, 10]));
        var sentId = set.Cards[0].PlaylistId!.Value;
        await LockAsync(sentId);

        var updated = await _sut.UpdateBreaksAsync(set.Id, new UpdateCardSetBreaksRequest([5, 8]));

        updated.Cards.Select(c => (c.FirstBookIndex, c.LastBookIndex)).Should().Equal((0, 4), (5, 7), (8, 14));
        updated.Cards[0].PlaylistId.Should().Be(sentId);
        updated.Cards[0].Locked.Should().BeTrue();
        updated.Cards[0].Status.Should().Be(PlaylistStatus.Transferred);
    }

    [Fact]
    public async Task UpdateBreaks_KeepsTheTitleOfACardWhoseBooksDidNotChange()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(15));
        var set = await _sut.CreateAsync(userId, Create(breaks: [5, 10]));
        await _sut.RenameCardAsync(set.Id, set.Cards[0].PlaylistId!.Value, "My favourites");

        var updated = await _sut.UpdateBreaksAsync(set.Id, new UpdateCardSetBreaksRequest([5, 8]));

        updated.Cards[0].Title.Should().Be("My favourites");
        updated.Cards[1].Title.Should().Be("Dragons · Books 6–8");
    }

    [Fact]
    public async Task UpdateBreaks_WithFewerCards_RemovesTheSpareUnsentCard()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(6));
        var set = await _sut.CreateAsync(userId, Create(breaks: [3]));

        var updated = await _sut.UpdateBreaksAsync(set.Id, new UpdateCardSetBreaksRequest([]));

        updated.Cards.Should().ContainSingle();
        (await CardsAsync(set.Id)).Should().ContainSingle();
    }

    // =========================================================================
    // Re-pack on mode or exclusion changes
    // =========================================================================

    [Fact]
    public async Task Update_ModeChange_RepacksOnlyTheCardsAfterTheLastSentOne()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(24));
        var set = await _sut.CreateAsync(userId, Create(PackingMode.Fill));
        set.Cards.Select(c => (c.FirstBookIndex, c.LastBookIndex)).Should().Equal((0, 9), (10, 19), (20, 23));
        await LockAsync(set.Cards[0].PlaylistId!.Value);

        var updated = await _sut.UpdateAsync(set.Id, new UpdateCardSetRequest(PackingMode: PackingMode.Balance));

        updated.PackingMode.Should().Be(PackingMode.Balance);
        updated.Cards.Select(c => (c.FirstBookIndex, c.LastBookIndex)).Should().Equal((0, 9), (10, 16), (17, 23));
        updated.Cards[0].Locked.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ExcludingABook_TakesItOffTheUnsentCards()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(4));
        var set = await _sut.CreateAsync(userId, Create());

        var updated = await _sut.UpdateAsync(set.Id, new UpdateCardSetRequest(ExcludedItemIds: ["b2"]));

        updated.ExcludedItemIds.Should().Equal("b2");
        updated.Books.Select(b => b.AbsLibraryItemId).Should().Equal("b1", "b3", "b4");
    }

    [Fact]
    public async Task Update_AfterAPartWasSent_ContinuesTheSplitBookFromTheNextChapter()
    {
        var userId = await SeedUserAsync();
        SetupSeries(new SeriesBookSpec("big", "4", Enumerable.Repeat(1.0, 25).ToArray()), new SeriesBookSpec("b5", "5", 1));
        var set = await _sut.CreateAsync(userId, Create(splitPoints: new() { ["big"] = [9, 17] }));
        await LockAsync(set.Cards[0].PlaylistId!.Value);

        var updated = await _sut.UpdateAsync(set.Id, new UpdateCardSetRequest(PackingMode: PackingMode.Fill));

        var parts = updated.Cards.Where(c => c.PartNumber is not null).ToList();
        parts.Select(p => (p.FirstChapterIndex, p.LastChapterIndex)).Should().Equal((0, 8), (9, 18), (19, 24));
        parts.Select(p => p.PartNumber).Should().Equal(1, 2, 3);
        updated.Cards.Last().Title.Should().Be("Dragons 5");
    }

    [Fact]
    public async Task Update_NameOnly_DoesNotTouchTheCards()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(4));
        var set = await _sut.CreateAsync(userId, Create());

        var updated = await _sut.UpdateAsync(set.Id, new UpdateCardSetRequest(Name: "Renamed"));

        updated.Name.Should().Be("Renamed");
        updated.Cards.Select(c => c.Title).Should().Equal(set.Cards.Select(c => c.Title));
        _absService.Verify(s => s.GetSeriesDetailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once); // only the create
    }

    // =========================================================================
    // Read, rename, delete
    // =========================================================================

    [Fact]
    public async Task List_FiltersBySeriesAndCountsSentCards()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());
        await LockAsync(set.Cards[0].PlaylistId!.Value);

        var mine = await _sut.ListAsync(userId, SeriesId);
        var other = await _sut.ListAsync(userId, "another-series");

        mine.Should().ContainSingle().Which.Should().Match<CardSetSummaryResponse>(s => s.CardCount == 2 && s.SentCount == 1);
        other.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ReportsStatusFromTheCards()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());
        var card = await _fixture.DbContext.Playlists.FirstAsync(p => p.Id == set.Cards[1].PlaylistId);
        card.Status = PlaylistStatus.Failed;
        await _fixture.DbContext.SaveChangesAsync();

        (await _sut.GetAsync(set.Id))!.Status.Should().Be(CardSetStatus.NeedsAttention);
    }

    [Fact]
    public async Task Delete_LeavesTheCardsAsStandalonePlaylists()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());

        await _sut.DeleteAsync(set.Id);

        (await _fixture.DbContext.CardSets.CountAsync()).Should().Be(0);
        var playlists = await _fixture.DbContext.Playlists.ToListAsync();
        playlists.Should().HaveCount(2).And.OnlyContain(p => p.CardSetId == null && p.SetIndex == null);
    }

    [Fact]
    public async Task RenameCard_ForACardOfAnotherSet_IsNotFound()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());

        var act = () => _sut.RenameCardAsync(set.Id, Guid.NewGuid(), "x");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // =========================================================================
    // Send
    // =========================================================================

    [Fact]
    public async Task PrepareTransfer_WithoutIds_PicksEveryUnsentCardInOrder()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(24));
        var set = await _sut.CreateAsync(userId, Create());
        await LockAsync(set.Cards[1].PlaylistId!.Value);

        var ids = await _sut.PrepareTransferAsync(set.Id, null);

        ids.Should().Equal(set.Cards[0].PlaylistId!.Value, set.Cards[2].PlaylistId!.Value);
    }

    [Fact]
    public async Task PrepareTransfer_ACardOverTheLimit_IsRefused()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        var item = await _fixture.DbContext.PlaylistItems.FirstAsync();
        item.TrackDurations = [20 * 3600];
        await _fixture.DbContext.SaveChangesAsync();

        var act = () => _sut.PrepareTransferAsync(set.Id, null);

        (await act.Should().ThrowAsync<CardSetCapacityException>())
            .Which.PlaylistId.Should().Be(set.Cards[0].PlaylistId!.Value);
    }

    [Fact]
    public async Task PrepareTransfer_ACardOfAnotherSet_IsNotFound()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());

        var act = () => _sut.PrepareTransferAsync(set.Id, [Guid.NewGuid()]);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
    [Fact]
    public async Task PrepareTransfer_QueuesTheCards_SoASecondSendPicksNoneOfThem()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());

        var first = await _sut.PrepareTransferAsync(set.Id, null);
        var second = await _sut.PrepareTransferAsync(set.Id, null);

        first.Should().HaveCount(2);
        second.Should().BeEmpty();
        (await CardsAsync(set.Id)).Should().OnlyContain(c => c.Status == PlaylistStatus.Queued);
        (await _sut.GetAsync(set.Id))!.Status.Should().Be(CardSetStatus.Sending);
    }

    [Fact]
    public async Task PrepareTransfer_AQueuedCardAskedForAgain_Conflicts()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        var card = set.Cards[0].PlaylistId!.Value;
        await _sut.PrepareTransferAsync(set.Id, [card]);

        var act = () => _sut.PrepareTransferAsync(set.Id, [card]);

        await act.Should().ThrowAsync<CardSetConflictException>();
    }

    [Fact]
    public async Task PrepareTransfer_AFailedCard_CanBeSentAgain()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        var cardId = set.Cards[0].PlaylistId!.Value;
        var card = await _fixture.DbContext.Playlists.FirstAsync(p => p.Id == cardId);
        card.Status = PlaylistStatus.Failed;
        card.ErrorMessage = "Yoto said no";
        await _fixture.DbContext.SaveChangesAsync();

        (await _sut.PrepareTransferAsync(set.Id, [cardId])).Should().Equal(cardId);
        card.Status.Should().Be(PlaylistStatus.Queued);
        card.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseQueued_PutsQueuedCardsBackToDraft()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        var ids = await _sut.PrepareTransferAsync(set.Id, null);

        await _sut.ReleaseQueuedAsync(ids);

        (await CardsAsync(set.Id)).Should().OnlyContain(c => c.Status == PlaylistStatus.Draft);
    }

    [Fact]
    public async Task Create_WithABookThatCannotBeSplit_IsRefusedUntilItIsLeftOut()
    {
        var userId = await SeedUserAsync();
        SetupSeries(new SeriesBookSpec("b1", "1", 1), new SeriesBookSpec("huge", "2", 12, 1));

        var refused = () => _sut.CreateAsync(userId, Create());
        var thrown = await refused.Should().ThrowAsync<CardSetPlanException>();
        thrown.Which.Plan.UnsplittableBookIds.Should().Equal("huge");

        var set = await _sut.CreateAsync(userId, Create(excluded: ["huge"]));
        set.Books.Select(b => b.AbsLibraryItemId).Should().Equal("b1");
    }

    [Fact]
    public async Task Update_AfterAPartWasSent_NumbersTheLaterBooksByTheirPlaceInTheSet()
    {
        var userId = await SeedUserAsync();
        SetupSeries(
            new SeriesBookSpec("b0", null, 1), new SeriesBookSpec("b1", null, 1), new SeriesBookSpec("b2", null, 1),
            new SeriesBookSpec("big", null, Enumerable.Repeat(1.0, 25).ToArray()),
            new SeriesBookSpec("c0", null, 1), new SeriesBookSpec("c1", null, 1), new SeriesBookSpec("c2", null, 1));
        var set = await _sut.CreateAsync(userId, Create());
        await LockAsync(set.Cards[0].PlaylistId!.Value);
        await LockAsync(set.Cards[1].PlaylistId!.Value);

        // Leaving c0 out changes the last card, so its title is worked out again: c1 and c2 are now
        // books 5 and 6 of the set.
        var updated = await _sut.UpdateAsync(set.Id, new UpdateCardSetRequest(ExcludedItemIds: ["c0"]));

        updated.Cards.Last().Title.Should().Be("Dragons · Books 5–6");
    }
    // =========================================================================
    // Phase 2: new books, status, limits
    // =========================================================================

    [Fact]
    public async Task Get_ListsBooksAddedToTheSeriesSinceTheSetWasMade()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create(excluded: ["b2"]));
        SetupSeries(HourBooks(4));

        var read = await _sut.GetAsync(set.Id);

        read!.NewBooks.Should().ContainSingle().Which.AbsLibraryItemId.Should().Be("b4");
    }

    [Fact]
    public async Task Get_WithoutNewBooks_DoesNotReadAudiobookshelf()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        _absService.Invocations.Clear();

        var read = await _sut.GetAsync(set.Id, withNewBooks: false);

        read!.NewBooks.Should().BeNull();
        _absService.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_WhenAudiobookshelfCannotBeRead_StillAnswersWithNoNewBooks()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), LibraryId, SeriesId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ABS down"));

        var read = await _sut.GetAsync(set.Id);

        read!.NewBooks.Should().BeEmpty();
        read.Cards.Should().ContainSingle();
    }

    [Fact]
    public async Task Status_ReportsOnlyTheAskedSeries_WithTheBooksTheSetKnows()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create(excluded: ["b3"]));
        await _sut.PrepareTransferAsync(set.Id, null);

        var status = await _sut.StatusAsync(userId, [SeriesId, "not-a-set"]);

        var one = status.Should().ContainSingle().Subject;
        one.CardSetId.Should().Be(set.Id);
        one.Sending.Should().BeTrue();
        one.KnownBookIds.Should().BeEquivalentTo(["b1", "b2", "b3"]);
    }

    [Fact]
    public async Task Status_ForAnotherUsersSeries_IsEmpty()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        await _sut.CreateAsync(userId, Create());

        (await _sut.StatusAsync(Guid.NewGuid(), [SeriesId])).Should().BeEmpty();
    }

    [Fact]
    public void Limits_AreTheConfiguredOnes()
    {
        _sut.Limits().Should().Be(new CardLimitsResponse(100, 10 * 45_000_000L, null, 100));
    }

    [Fact]
    public async Task Refresh_WithNoNewBooks_SaysSo()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());

        var refreshed = await _sut.RefreshAsync(set.Id);

        refreshed.Messages.Should().Equal("No new books in this series.");
        refreshed.Cards.Should().HaveCount(1);
    }

    [Fact]
    public async Task Refresh_AddsNewBooksToTheLastCardWhileItIsNotOnYoto()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        SetupSeries(HourBooks(4));

        var refreshed = await _sut.RefreshAsync(set.Id);

        refreshed.Cards.Should().ContainSingle();
        refreshed.Books.Select(b => b.AbsLibraryItemId).Should().Equal("b1", "b2", "b3", "b4");
        refreshed.Cards[0].Title.Should().Be("Dragons · Books 1–4");
        refreshed.NewBooks.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_WhenTheLastCardIsOnYoto_PutsNewBooksOnANewCard()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(3));
        var set = await _sut.CreateAsync(userId, Create());
        await LockAsync(set.Cards[0].PlaylistId!.Value);
        SetupSeries(HourBooks(5));

        var refreshed = await _sut.RefreshAsync(set.Id);

        refreshed.Cards.Select(c => (c.FirstBookIndex, c.LastBookIndex)).Should().Equal((0, 2), (3, 4));
        refreshed.Cards[0].Locked.Should().BeTrue();
        refreshed.Cards[1].Title.Should().Be("Dragons · Books 4–5");
    }

    [Fact]
    public async Task Refresh_ABookEarlierInTheSeries_GoesOnTheFirstCardThatCanChange_AndSaysSo()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(12));
        var set = await _sut.CreateAsync(userId, Create());
        var firstCard = set.Cards[0];
        SetupSeries([.. HourBooks(12), new SeriesBookSpec("b3.5", "3.5", 1)]);

        var refreshed = await _sut.RefreshAsync(set.Id);

        refreshed.Cards[0].PlaylistId.Should().Be(firstCard.PlaylistId);
        refreshed.Cards[0].Title.Should().Be(firstCard.Title);
        refreshed.Books.Select(b => b.AbsLibraryItemId).Skip(6).First().Should().Be("b3.5");
        refreshed.Messages.Should().ContainSingle().Which.Should().Contain("Title b3.5").And.Contain("card 2");
    }

    [Fact]
    public async Task Refresh_ANewBookThatCannotBeSplit_IsRefused()
    {
        var userId = await SeedUserAsync();
        SetupSeries(HourBooks(2));
        var set = await _sut.CreateAsync(userId, Create());
        SetupSeries([.. HourBooks(2), new SeriesBookSpec("huge", "3", 12, 1)]);

        var act = () => _sut.RefreshAsync(set.Id);

        await act.Should().ThrowAsync<CardSetPlanException>();
    }
}
