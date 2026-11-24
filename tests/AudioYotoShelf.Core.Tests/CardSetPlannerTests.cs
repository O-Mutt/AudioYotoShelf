using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Services;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class CardSetPlannerTests
{
    private const double Hour = 3600;
    private const long BytesPerHour = 45_000_000; // 1 h at the default 100 kbps estimate

    // A 10-hour card keeps the arithmetic readable; the real default is about 11.6 h.
    private static YotoCardLimits TenHourCard() => new() { MaxTotalBytes = 10 * BytesPerHour };

    private static CardSetPlanner Planner(YotoCardLimits limits) =>
        new(new CardCapacityCalculator(limits), new TrackPlanner(limits), limits);

    private readonly CardSetPlanner _sut = Planner(TenHourCard());

    /// <summary>A book whose chapters last the given number of hours.</summary>
    private static BookTrackPlan Book(string id, params double[] chapterHours) =>
        new(id, id, chapterHours.Select((h, i) => new SourceTrack($"{id} ch{i + 1}", h * Hour, 1)).ToList());

    private static BookTrackPlan[] Books(params double[] hours) =>
        hours.Select((h, i) => Book($"b{i}", h)).ToArray();

    private static (int, int)[] Ranges(CardSetPlan plan) =>
        plan.Cards.Select(c => (c.FirstBookIndex, c.LastBookIndex)).ToArray();

    private static (int?, int?)[] Parts(CardSetPlan plan) =>
        plan.Cards.Where(c => c.IsPart).Select(c => (c.FirstChapterIndex, c.LastChapterIndex)).ToArray();

    // =========================================================================
    // Fill
    // =========================================================================

    [Fact]
    public void Fill_EmptySeries_HasNoCards()
    {
        var plan = _sut.Plan([], PackingMode.Fill);

        plan.Cards.Should().BeEmpty();
        plan.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public void Fill_OneBook_IsOneCard()
    {
        Ranges(_sut.Plan(Books(3), PackingMode.Fill)).Should().Equal((0, 0));
    }

    [Fact]
    public void Fill_BooksThatFitTogether_ShareOneCard()
    {
        Ranges(_sut.Plan(Books(1, 2, 3), PackingMode.Fill)).Should().Equal((0, 2));
    }

    [Fact]
    public void Fill_ExactlyFullCard_StaysOneCard()
    {
        var plan = _sut.Plan(Books(5, 5), PackingMode.Fill);

        Ranges(plan).Should().Equal((0, 1));
        plan.Cards[0].Capacity.Projected.Bytes.Should().Be(10 * BytesPerHour);
    }

    [Fact]
    public void Fill_OneByteOver_StartsASecondCard()
    {
        var sut = Planner(new YotoCardLimits { MaxTotalBytes = 10 * BytesPerHour - 1 });

        Ranges(sut.Plan(Books(5, 5), PackingMode.Fill)).Should().Equal((0, 0), (1, 1));
    }

    [Fact]
    public void Fill_FillsEachCardBeforeStartingTheNext()
    {
        Ranges(_sut.Plan(Books(Enumerable.Repeat(1.0, 12).ToArray()), PackingMode.Fill))
            .Should().Equal((0, 9), (10, 11));
    }

    // =========================================================================
    // Balance
    // =========================================================================

    [Fact]
    public void Balance_UsesFillsCardCount_AndEvensTheCards()
    {
        var books = Books(Enumerable.Repeat(1.0, 12).ToArray());

        var fill = _sut.Plan(books, PackingMode.Fill);
        var balance = _sut.Plan(books, PackingMode.Balance);

        balance.Cards.Should().HaveCount(fill.Cards.Count);
        Ranges(balance).Should().Equal((0, 5), (6, 11));
    }

    [Theory]
    [InlineData(new[] { 4.0, 3, 2, 5, 1, 6, 2, 2, 3, 4, 1, 1 })]
    [InlineData(new[] { 9.0, 1, 1, 1, 1, 9 })]
    [InlineData(new[] { 2.0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 })]
    public void Balance_IsNeverFullerThanFill(double[] hours)
    {
        var books = Books(hours);

        var fill = _sut.Plan(books, PackingMode.Fill);
        var balance = _sut.Plan(books, PackingMode.Balance);

        balance.Cards.Should().HaveCount(fill.Cards.Count);
        balance.Cards.Max(c => c.Capacity.Projected.Bytes)
            .Should().BeLessThanOrEqualTo(fill.Cards.Max(c => c.Capacity.Projected.Bytes));
        balance.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public void Balance_OnATie_PutsTheBreakLater()
    {
        // [4][1,4] and [4,1][4] both have a 5 h fullest card; the later break leaves the last card emptier.
        var sut = Planner(new YotoCardLimits { MaxTotalBytes = 6 * BytesPerHour });

        Ranges(sut.Plan(Books(4, 1, 4), PackingMode.Balance)).Should().Equal((0, 1), (2, 2));
    }

    [Fact]
    public void Balance_TheTrackLimitCanBindBeforeSize()
    {
        // Each book is two 40-minute chapters: over an hour, so Auto keeps both tracks.
        var sut = Planner(new YotoCardLimits { MaxTotalBytes = 10 * BytesPerHour, MaxTracks = 4 });
        var books = Enumerable.Range(0, 5).Select(i => Book($"b{i}", 40.0 / 60, 40.0 / 60)).ToArray();

        var plan = sut.Plan(books, PackingMode.Balance);

        plan.Cards.Should().HaveCount(3);
        plan.Cards.Should().OnlyContain(c => c.Capacity.Projected.Tracks <= 4);
    }

    [Fact]
    public void Plan_ShortBooksMergeToOneTrackUnderAuto()
    {
        var books = Enumerable.Range(0, 3).Select(i => Book($"b{i}", 0.25, 0.25)).ToArray();

        _sut.Plan(books, PackingMode.Fill).Cards[0].Capacity.Projected.Tracks.Should().Be(3);
    }

    // =========================================================================
    // Apply
    // =========================================================================

    [Fact]
    public void Apply_ValidBreaks_MakeThoseCards()
    {
        var plan = _sut.Apply(Books(1, 1, 1, 1), [2], new Dictionary<string, IReadOnlyList<int>>());

        Ranges(plan).Should().Equal((0, 1), (2, 3));
        plan.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public void Apply_ABreakThatOverflowsACard_IsReportedNotFixed()
    {
        var plan = _sut.Apply(Books(4, 4, 4, 1), [3], new Dictionary<string, IReadOnlyList<int>>());

        Ranges(plan).Should().Equal((0, 2), (3, 3));
        plan.WithinLimits.Should().BeFalse();
        plan.Cards[0].Capacity.WithinLimits.Should().BeFalse();
        plan.Messages.Should().ContainSingle().Which.Should().StartWith("Card 1 does not fit");
    }

    [Theory]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new[] { 2, 2 })]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 4 })]
    public void Apply_BreaksOutOfOrderOrRange_Throw(int[] breaks)
    {
        var act = () => _sut.Apply(Books(1, 1, 1, 1), breaks, new Dictionary<string, IReadOnlyList<int>>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Apply_AddsBreaksAroundABookThatMustBeSplit()
    {
        var books = new[] { Book("a", 1), Book("big", Enumerable.Repeat(1.0, 15).ToArray()), Book("c", 1) };

        var plan = _sut.Apply(books, [], new Dictionary<string, IReadOnlyList<int>>());

        plan.Cards.First().Should().Match<PlannedCard>(c => c.FirstBookIndex == 0 && c.LastBookIndex == 0);
        plan.Cards.Last().Should().Match<PlannedCard>(c => c.FirstBookIndex == 2 && c.LastBookIndex == 2);
        plan.Cards.Where(c => c.FirstBookIndex == 1).Should().OnlyContain(c => c.IsPart);
    }

    // =========================================================================
    // Splitting books larger than one card
    // =========================================================================

    [Fact]
    public void Split_ABookOverOneCard_SplitsIntoTheFewestParts()
    {
        var plan = _sut.Plan([Book("big", Enumerable.Repeat(1.0, 25).ToArray())], PackingMode.Balance);

        plan.Cards.Should().HaveCount(3).And.OnlyContain(c => c.IsPart && c.PartCount == 3);
        plan.Cards.Select(c => c.PartNumber).Should().Equal(1, 2, 3);
        plan.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public void Split_PartsCoverEveryChapterOnce_AndAreBalanced()
    {
        var plan = _sut.Plan([Book("big", Enumerable.Repeat(1.0, 25).ToArray())], PackingMode.Balance);

        var parts = Parts(plan);
        parts.First().Item1.Should().Be(0);
        parts.Last().Item2.Should().Be(24);
        for (var i = 1; i < parts.Length; i++)
            parts[i].Item1.Should().Be(parts[i - 1].Item2 + 1);
        plan.Cards.Max(c => c.Capacity.Projected.Bytes).Should().BeLessThanOrEqualTo(9 * BytesPerHour);
    }

    [Fact]
    public void Split_FillPacksPartsFullFirst()
    {
        var plan = _sut.Plan([Book("big", Enumerable.Repeat(1.0, 25).ToArray())], PackingMode.Fill);

        Parts(plan).Should().Equal((0, 9), (10, 19), (20, 24));
    }

    [Fact]
    public void Split_NeighboursNeverShareASplitBooksCards()
    {
        var books = new[] { Book("a", 1), Book("big", Enumerable.Repeat(1.0, 15).ToArray()), Book("c", 1) };

        var plan = _sut.Plan(books, PackingMode.Balance);

        plan.Cards.Should().HaveCount(4);
        Ranges(plan).Should().Equal((0, 0), (1, 1), (1, 1), (2, 2));
    }

    [Fact]
    public void Split_BooksOnEachSidePackOnTheirOwn()
    {
        var books = new[]
        {
            Book("a", 1), Book("b", 1),
            Book("big", Enumerable.Repeat(1.0, 15).ToArray()),
            Book("d", 1), Book("e", 1),
        };

        Ranges(_sut.Plan(books, PackingMode.Balance)).Should().Equal((0, 1), (2, 2), (2, 2), (3, 4));
    }

    [Fact]
    public void Split_ABookWithoutChapters_IsCutIntoEqualSegmentsOfOneTrackAtMost()
    {
        var plan = _sut.Plan([Book("nochapters", 25)], PackingMode.Fill);

        var segments = plan.SplitChapters["nochapters"];
        segments.Should().HaveCount(25).And.OnlyContain(s => s.DurationSeconds == Hour);
        Parts(plan).Should().Equal((0, 9), (10, 19), (20, 24));
    }

    [Fact]
    public void Split_AChapterBiggerThanACard_LeavesTheBookOut()
    {
        var books = new[] { Book("a", 1), Book("huge", 12, 1), Book("c", 1) };

        var plan = _sut.Plan(books, PackingMode.Balance);

        plan.UnsplittableBookIds.Should().Equal("huge");
        plan.Messages.Should().ContainSingle().Which.Should().Contain("huge");
        plan.Cards.Should().NotContain(c => c.FirstBookIndex == 1);
        Ranges(plan).Should().Equal((0, 0), (2, 2));
    }

    [Fact]
    public void Apply_ValidSplitPoints_MakeThoseParts()
    {
        var books = new[] { Book("big", Enumerable.Repeat(1.0, 25).ToArray()) };

        var plan = _sut.Apply(books, [], new Dictionary<string, IReadOnlyList<int>> { ["big"] = [8, 17] });

        Parts(plan).Should().Equal((0, 7), (8, 16), (17, 24));
        plan.WithinLimits.Should().BeTrue();
    }

    [Fact]
    public void Apply_ASplitPointThatOverflowsAPart_IsReported()
    {
        var books = new[] { Book("big", Enumerable.Repeat(1.0, 20).ToArray()) };

        var plan = _sut.Apply(books, [], new Dictionary<string, IReadOnlyList<int>> { ["big"] = [15] });

        plan.WithinLimits.Should().BeFalse();
        plan.Cards[0].Capacity.WithinLimits.Should().BeFalse();
    }

    [Fact]
    public void Apply_SplitPointsOutOfRange_Throw()
    {
        var books = new[] { Book("big", Enumerable.Repeat(1.0, 20).ToArray()) };

        var act = () => _sut.Apply(books, [], new Dictionary<string, IReadOnlyList<int>> { ["big"] = [20] });

        act.Should().Throw<ArgumentException>();
    }

    // =========================================================================
    // Standalone books and limits
    // =========================================================================

    [Fact]
    public void Plan_AStandaloneBookThatFits_GetsOneWholeCardOfItsOwn()
    {
        var plan = _sut.Plan(Books(1, 1, 1), PackingMode.Fill, standalone: new HashSet<int> { 0 });

        Ranges(plan).Should().Equal((0, 0), (1, 2));
        plan.Cards[0].IsPart.Should().BeFalse();
    }

    [Fact]
    public void Plan_ChangingTheCardLimit_ChangesOnlyThePacking()
    {
        var books = Books(Enumerable.Repeat(1.0, 12).ToArray());
        var twentyHours = Planner(new YotoCardLimits { MaxTotalBytes = 20 * BytesPerHour });

        Ranges(_sut.Plan(books, PackingMode.Fill)).Should().Equal((0, 9), (10, 11));
        Ranges(twentyHours.Plan(books, PackingMode.Fill)).Should().Equal((0, 11));
    }
}
