using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Services;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class CardCapacityCalculatorTests
{
    private readonly YotoCardLimits _limits = new();
    private readonly CardCapacityCalculator _sut;

    public CardCapacityCalculatorTests() => _sut = new CardCapacityCalculator(_limits);

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
    public void Calculate_TooManyTracks_FlagsTracksOnly()
    {
        var tracks = Enumerable.Range(0, 101)
            .Select(i => new SourceTrack($"c{i}", 60, 1_000_000))
            .ToArray();

        var result = _sut.Calculate([Book("b1", tracks)]);

        result.WithinLimits.Should().BeFalse();
        result.ExceedsTracks.Should().BeTrue();
        result.ExceedsDuration.Should().BeFalse();
        result.ExceedsBytes.Should().BeFalse();
    }

    [Fact]
    public void Calculate_TooLong_FlagsDuration()
    {
        // 6 one-hour tracks = 6h > 5h limit; each is exactly 1h so no split, 6 tracks total.
        var tracks = Enumerable.Range(0, 6)
            .Select(i => new SourceTrack($"c{i}", 3600, 1_000_000))
            .ToArray();

        var result = _sut.Calculate([Book("b1", tracks)]);

        result.ExceedsDuration.Should().BeTrue();
        result.ExceedsTracks.Should().BeFalse();
        result.Projected.Tracks.Should().Be(6);
    }

    [Fact]
    public void Calculate_TooLarge_FlagsBytes()
    {
        var result = _sut.Calculate([Book("b1",
            new SourceTrack("c1", 60, 600L * 1024 * 1024))]); // 600 MB

        result.ExceedsBytes.Should().BeTrue();
        result.ExceedsDuration.Should().BeFalse();
    }

    [Fact]
    public void Calculate_ExactlyAtLimits_IsWithin()
    {
        // 5 tracks of exactly 1h = 5h, 100 MB each = 500 MB. All exactly at the line.
        var tracks = Enumerable.Range(0, 5)
            .Select(i => new SourceTrack($"c{i}", 3600, 100L * 1024 * 1024))
            .ToArray();

        var result = _sut.Calculate([Book("b1", tracks)]);

        result.WithinLimits.Should().BeTrue();
        result.Projected.Tracks.Should().Be(5);
    }

    [Fact]
    public void Calculate_ReportsFirstBookThatOverflows()
    {
        var fits = Book("book-1", new SourceTrack("c", 3600, 1_000_000));      // 1h
        var overflows = Book("book-2",
            Enumerable.Range(0, 5).Select(i => new SourceTrack($"c{i}", 3600, 1_000_000)).ToArray()); // +5h

        var result = _sut.Calculate([fits, overflows]);

        result.ExceedsDuration.Should().BeTrue();
        result.FirstOverflowItemId.Should().Be("book-2");
    }
}
