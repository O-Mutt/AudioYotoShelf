using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Services;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class CardCapacityCalculatorTests
{
    private readonly YotoCardLimits _limits = new();
    private readonly CardCapacityCalculator _sut;

    private readonly CardCapacityCalculator _withDurationCap =
        new(new YotoCardLimits { MaxTotalDurationSeconds = 5 * 3600 });

    public CardCapacityCalculatorTests() => _sut = new CardCapacityCalculator(_limits);

    private const double Hour = 3600;

    private static BookTrackPlan Book(string id, params SourceTrack[] tracks) => new(id, id, tracks);

    // =========================================================================
    // ProjectedTrackCount — splitting math
    // =========================================================================

    [Theory]
    [InlineData(3600, 1)]    // exactly 1 hour -> 1 track
    [InlineData(3601, 2)]    // just over 1 hour -> 2 tracks
    [InlineData(5400, 2)]    // 90 min -> 2 tracks
    [InlineData(9000, 3)]    // 2.5 h -> 3 tracks
    [InlineData(60, 1)]      // short -> 1 track
    public void ProjectedTrackCount_SplitsByDuration(double seconds, int expected)
    {
        _sut.ProjectedTrackCount(new SourceTrack("t", seconds, 0)).Should().Be(expected);
    }

    [Fact]
    public void ProjectedTrackCount_SplitsByBytes()
    {
        var track = new SourceTrack("t", 60, 250L * 1024 * 1024); // 250 MB, 1 min
        _sut.ProjectedTrackCount(track).Should().Be(3);           // ceil(250/100)
    }

    [Fact]
    public void ProjectedTrackCount_NeverBelowOne()
    {
        _sut.ProjectedTrackCount(new SourceTrack("t", 0, 0)).Should().Be(1);
    }

    // =========================================================================
    // Calculate — aggregate limits
    // =========================================================================

    private static SourceTrack[] Tracks(int count, double seconds, long bytes) =>
        Enumerable.Range(0, count).Select(i => new SourceTrack($"c{i}", seconds, bytes)).ToArray();

    [Fact]
    public void Calculate_Empty_IsWithinLimits()
    {
        var result = _sut.Calculate([]);

        result.WithinLimits.Should().BeTrue();
        result.Projected.Should().Be(new CapacityUsage(0, 0, 0));
        result.FirstOverflowItemId.Should().BeNull();
    }

    [Fact]
    public void Calculate_SmallBook_IsWithinLimits()
    {
        var result = _sut.Calculate([Book("b1",
            new SourceTrack("c1", 600, 10_000_000),
            new SourceTrack("c2", 600, 10_000_000))]);

        result.WithinLimits.Should().BeTrue();
        result.Projected.Tracks.Should().Be(2);
        result.ExceedsTracks.Should().BeFalse();
    }

    [Fact]
    public void Calculate_EstimatesBytesFromDurationAtTheTranscodedBitrate()
    {
        // 1 h at 100 kbps = 45,000,000 bytes, whatever the source file weighs.
        var result = _sut.Calculate([Book("b1", new SourceTrack("c", Hour, 999_999_999))]);

        result.Projected.Bytes.Should().Be(45_000_000);
    }

    [Fact]
    public void Calculate_TrackWithoutDuration_CountsAtItsSourceSize()
    {
        // Audiobookshelf reports 0 for unscanned files; 600 MB of them must not weigh nothing.
        var result = _sut.Calculate([Book("b1", Tracks(6, 0, 100L * 1024 * 1024))]);

        result.Projected.Bytes.Should().Be(600L * 1024 * 1024);
        result.ExceedsBytes.Should().BeTrue();
    }

    [Theory]
    [InlineData(TrackGrouping.Chapters)]
    [InlineData(TrackGrouping.SingleTrack)]
    public void Calculate_UnscannedFiles_WeighTheSameWhateverTheGrouping(TrackGrouping grouping)
    {
        // Merging must not fold the unscanned files' source size into the scanned duration.
        var chapters = Tracks(10, Hour, 1_000_000).Concat(Tracks(5, 0, 100_000_000)).ToArray();
        var grouped = new TrackPlanner(_limits).ApplyGrouping(chapters, grouping, "b1");

        var result = _sut.Calculate([new BookTrackPlan("b1", "b1", grouped)]);

        result.Projected.Bytes.Should().Be(10 * 45_000_000L + 5 * 100_000_000L);
        result.ExceedsBytes.Should().BeTrue();
    }

    [Fact]
    public void Calculate_WithoutSplitting_CountsEachSourceTrackOnce()
    {
        // 60 tracks of 90 min / 200 MB: split, each becomes 2 pieces and the card is over 100 tracks.
        var book = Book("b1", Tracks(60, 1.5 * Hour, 200L * 1024 * 1024));

        _sut.Calculate([book]).Projected.Tracks.Should().Be(120);
        var unsplit = _sut.Calculate([book], splitTracks: false);
        unsplit.Projected.Tracks.Should().Be(60);
        unsplit.ExceedsTracks.Should().BeFalse();
    }

    [Fact]
    public void Calculate_TooManyTracks_FlagsTracksOnly()
    {
        var result = _sut.Calculate([Book("b1", Tracks(101, 60, 1_000_000))]);

        result.WithinLimits.Should().BeFalse();
        result.ExceedsTracks.Should().BeTrue();
        result.ExceedsDuration.Should().BeFalse();
        result.ExceedsBytes.Should().BeFalse();
    }

    [Fact]
    public void Calculate_LongCardWithinSize_IsWithinByDefault()
    {
        // Prisoner of Azkaban as Yoto accepted it: 11.54 h in 24 tracks, 629.5 MB of source audio.
        var result = _sut.Calculate([Book("hp3", Tracks(24, 11.54 * Hour / 24, 629_500_000 / 24))]);

        result.WithinLimits.Should().BeTrue();
        result.ExceedsDuration.Should().BeFalse();
        result.ExceedsBytes.Should().BeFalse();
    }

    [Fact]
    public void Calculate_TooLarge_FlagsBytes()
    {
        // 12 h at 100 kbps = 540,000,000 bytes (515 MB) > 500 MB.
        var result = _sut.Calculate([Book("b1", Tracks(12, Hour, 1_000_000))]);

        result.ExceedsBytes.Should().BeTrue();
        result.ExceedsDuration.Should().BeFalse();
    }

    [Fact]
    public void Calculate_DurationCapConfigured_FlagsDuration()
    {
        var result = _withDurationCap.Calculate([Book("b1", Tracks(6, Hour, 1_000_000))]);

        result.ExceedsDuration.Should().BeTrue();
        result.ExceedsTracks.Should().BeFalse();
        result.ExceedsBytes.Should().BeFalse();
    }

    [Fact]
    public void Calculate_ReportsFirstBookThatOverflows()
    {
        var fits = Book("book-1", new SourceTrack("c", Hour, 1_000_000));
        var overflows = Book("book-2", Tracks(11, Hour, 1_000_000)); // 12 h in total

        var result = _sut.Calculate([fits, overflows]);

        result.ExceedsBytes.Should().BeTrue();
        result.FirstOverflowItemId.Should().Be("book-2");
    }

    // =========================================================================
    // Exact boundaries and the user-facing messages
    // =========================================================================

    [Fact]
    public void Calculate_ExactlyMaxTracks_DoesNotFlagAnyBookAsOverflow()
    {
        var result = _sut.Calculate([Book("b1", Tracks(100, 60, 1_000_000))]);

        result.ExceedsTracks.Should().BeFalse();
        result.FirstOverflowItemId.Should().BeNull();
    }

    [Fact]
    public void Calculate_ExactlyMaxDuration_DoesNotFlagAnyBookAsOverflow()
    {
        var result = _withDurationCap.Calculate([Book("b1", Tracks(5, Hour, 1_000_000))]);

        result.ExceedsDuration.Should().BeFalse();
        result.FirstOverflowItemId.Should().BeNull();
    }

    [Fact]
    public void Calculate_ExactlyMaxBytes_DoesNotFlagAnyBookAsOverflow()
    {
        var sut = new CardCapacityCalculator(new YotoCardLimits { MaxTotalBytes = 45_000_000 });

        var result = sut.Calculate([Book("b1", new SourceTrack("c", Hour, 1))]); // exactly 45,000,000

        result.ExceedsBytes.Should().BeFalse();
        result.FirstOverflowItemId.Should().BeNull();
    }

    [Fact]
    public void Calculate_OneTrackOverMax_FlagsTheBook()
    {
        var result = _sut.Calculate([Book("b1", Tracks(101, 60, 1_000_000))]);

        result.ExceedsTracks.Should().BeTrue();
        result.FirstOverflowItemId.Should().Be("b1");
    }

    [Fact]
    public void Calculate_OneSecondOverMaxDuration_FlagsTheBook()
    {
        var result = _withDurationCap.Calculate([Book("b1", [.. Tracks(5, Hour, 1_000_000), new SourceTrack("extra", 1, 1_000_000)])]);

        result.ExceedsDuration.Should().BeTrue();
        result.FirstOverflowItemId.Should().Be("b1");
    }

    [Fact]
    public void Calculate_OneSecondOverMaxBytes_FlagsTheBook()
    {
        var sut = new CardCapacityCalculator(new YotoCardLimits { MaxTotalBytes = 45_000_000 });

        var result = sut.Calculate([Book("b1", new SourceTrack("c", Hour, 1), new SourceTrack("extra", 1, 1))]);

        result.ExceedsBytes.Should().BeTrue();
        result.FirstOverflowItemId.Should().Be("b1");
    }

    [Fact]
    public void Calculate_SeveralBooksOverflow_ReportsTheFirstOne()
    {
        var fits = Book("fits", new SourceTrack("c", Hour, 1_000_000));
        var firstOver = Book("first-over", Tracks(12, Hour, 1_000_000));
        var secondOver = Book("second-over", new SourceTrack("c", 60, 1_000_000));

        _sut.Calculate([fits, firstOver, secondOver]).FirstOverflowItemId.Should().Be("first-over");
    }

    [Fact]
    public void Calculate_WithinLimits_HasNoMessages()
    {
        _sut.Calculate([Book("b1", Tracks(2, 60, 1_000_000))]).Messages.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_TooManyTracks_ExplainsTheTrackLimit()
    {
        var result = _sut.Calculate([Book("b1", Tracks(101, 60, 1_000_000))]);

        result.Messages.Should().Equal("Track count 101 exceeds the limit of 100.");
    }

    [Fact]
    public void Calculate_TooLong_ExplainsTheDurationLimit()
    {
        var result = _withDurationCap.Calculate([Book("b1", Tracks(6, Hour, 1_000_000))]);

        result.Messages.Should().Equal("Total duration 6.0h exceeds the limit of 5.0h.");
    }

    [Fact]
    public void Calculate_TooLarge_ExplainsTheSizeLimit()
    {
        var result = _sut.Calculate([Book("b1", Tracks(12, Hour, 1_000_000))]);

        result.Messages.Should().Equal("Estimated size 515MB exceeds the limit of 500MB.");
    }

    [Fact]
    public void Calculate_ExceedingEveryLimit_ListsTracksThenDurationThenSize()
    {
        // 101 one-hour tracks: 101 tracks, 101 h, 4,545,000,000 bytes (4334 MB).
        var result = _withDurationCap.Calculate([Book("b1", Tracks(101, Hour, 1_000_000))]);

        result.WithinLimits.Should().BeFalse();
        result.Messages.Should().Equal(
            "Track count 101 exceeds the limit of 100.",
            "Total duration 101.0h exceeds the limit of 5.0h.",
            "Estimated size 4334MB exceeds the limit of 500MB.");
    }
}
