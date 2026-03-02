using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Services;
using AudioYotoShelf.Core.Tests.Helpers;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class AgeSuggestionServiceTests
{
    private readonly AgeSuggestionService _sut = new();

    // =========================================================================
    // Genre-based inference
    // =========================================================================

    [Theory]
    [InlineData(new[] { "Children's Fiction" }, 2, 8)]
    [InlineData(new[] { "Fairy Tales" }, 2, 8)]
    [InlineData(new[] { "Bedtime Stories" }, 2, 8)]
    public void SuggestAgeRange_ChildrenGenres_ReturnsYoungRange(string[] genres, int expectedMinLow, int expectedMaxHigh)
    {
        var metadata = TestData.CreateAbsMetadata(genres: genres);
        var result = _sut.SuggestAgeRange(metadata, 3600, 10);

        result.SuggestedMinAge.Should().BeInRange(expectedMinLow, 6);
        result.SuggestedMaxAge.Should().BeInRange(5, expectedMaxHigh);
        result.Source.Should().Be(AgeRangeSource.GenreInferred);
    }

    [Theory]
    [InlineData(new[] { "Middle Grade" })]
    [InlineData(new[] { "Chapter Book" })]
    public void SuggestAgeRange_MiddleGradeGenres_ReturnsMidRange(string[] genres)
    {
        var metadata = TestData.CreateAbsMetadata(genres: genres);
        var result = _sut.SuggestAgeRange(metadata, 18000, 15);

        result.SuggestedMinAge.Should().BeInRange(4, 10);
        result.SuggestedMaxAge.Should().BeInRange(8, 14);
    }

    [Theory]
    [InlineData(new[] { "Young Adult" })]
    [InlineData(new[] { "YA Fiction" })]
    [InlineData(new[] { "Coming of Age" })]
    public void SuggestAgeRange_YoungAdultGenres_ReturnsTeenRange(string[] genres)
    {
        var metadata = TestData.CreateAbsMetadata(genres: genres);
        var result = _sut.SuggestAgeRange(metadata, 36000, 25);

        result.SuggestedMinAge.Should().BeGreaterOrEqualTo(6);
        result.SuggestedMaxAge.Should().BeGreaterOrEqualTo(10);
    }

    [Theory]
    [InlineData(new[] { "Thriller" })]
    [InlineData(new[] { "Horror" })]
    [InlineData(new[] { "Crime Fiction" })]
    public void SuggestAgeRange_AdultGenres_ReturnsOlderRange(string[] genres)
    {
        var metadata = TestData.CreateAbsMetadata(genres: genres);
        var result = _sut.SuggestAgeRange(metadata, 36000, 30);

        result.SuggestedMinAge.Should().BeGreaterOrEqualTo(6);
    }

    // =========================================================================
    // Keyword-based inference
    // =========================================================================

    [Theory]
    [InlineData("A story about a princess and her magical unicorn", "princess")]
    [InlineData("Adventures of a brave dragon slayer wizard", "dragon")]
    public void SuggestAgeRange_DescriptionKeywords_DetectsSignals(string description, string expectedKeyword)
    {
        var metadata = TestData.CreateAbsMetadata(genres: [], description: description);
        var result = _sut.SuggestAgeRange(metadata, 3600, 10);

        result.Signals.Should().Contain(s => s.Signal == "Keyword");
        result.Signals.Should().Contain(s =>
            s.Value.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestAgeRange_WarKeywords_PushesAgeUp()
    {
        var metadata = TestData.CreateAbsMetadata(
            genres: [],
            description: "A harrowing tale of war and death in the trenches");
        var result = _sut.SuggestAgeRange(metadata, 36000, 30);

        result.SuggestedMinAge.Should().BeGreaterOrEqualTo(6);
    }

    // =========================================================================
    // Duration-based inference
    // =========================================================================

    [Theory]
    [InlineData(600, 2, 5)]      // 10 minutes → very young
    [InlineData(5400, 4, 8)]     // 90 minutes → young children
    [InlineData(14400, 6, 12)]   // 4 hours → middle grade
    [InlineData(43200, 8, 18)]   // 12 hours → older
    public void SuggestAgeRange_DurationHeuristic_AdjustsRange(
        double durationSeconds, int expectedMinLow, int expectedMaxHigh)
    {
        var metadata = TestData.CreateAbsMetadata(genres: []);
        var result = _sut.SuggestAgeRange(metadata, durationSeconds, 10);

        result.Signals.Should().Contain(s => s.Signal == "Duration");
        result.SuggestedMinAge.Should().BeGreaterOrEqualTo(expectedMinLow - 2);
        result.SuggestedMaxAge.Should().BeLessThanOrEqualTo(expectedMaxHigh + 2);
    }

    // =========================================================================
    // Explicit content flag
    // =========================================================================

    [Fact]
    public void SuggestAgeRange_ExplicitContent_EnforcesHighMinAge()
    {
        var metadata = TestData.CreateAbsMetadata(isExplicit: true);
        var result = _sut.SuggestAgeRange(metadata, 28800, 30);

        result.SuggestedMinAge.Should().BeGreaterOrEqualTo(8);
        result.Signals.Should().Contain(s => s.Signal == "ExplicitContent" && s.Weight >= 90);
    }

    [Fact]
    public void SuggestAgeRange_ExplicitOverridesChildrenGenre_CompromisesRange()
    {
        var metadata = TestData.CreateAbsMetadata(
            genres: ["Children's Fiction"], isExplicit: true);
        var result = _sut.SuggestAgeRange(metadata, 3600, 10);

        // Explicit flag should pull the weighted average up significantly
        result.SuggestedMinAge.Should().BeGreaterOrEqualTo(4);
    }

    // =========================================================================
    // Default fallback
    // =========================================================================

    [Fact]
    public void SuggestAgeRange_NoSignals_ReturnsDefaultRange()
    {
        var metadata = TestData.CreateAbsMetadata(genres: [], description: "");
        var result = _sut.SuggestAgeRange(metadata, 7200, 10);

        result.SuggestedMinAge.Should().BeInRange(0, 10);
        result.SuggestedMaxAge.Should().BeInRange(5, 18);
    }

    // =========================================================================
    // Edge cases and invariants
    // =========================================================================

    [Fact]
    public void SuggestAgeRange_Always_MinIsLessThanMax()
    {
        var testCases = new[]
        {
            TestData.CreateAbsMetadata(genres: ["Children"], isExplicit: true),
            TestData.CreateAbsMetadata(genres: ["Horror", "Children"]),
            TestData.CreateAbsMetadata(genres: [], description: "war princess baby"),
        };

        foreach (var metadata in testCases)
        {
            var result = _sut.SuggestAgeRange(metadata, 3600, 10);
            result.SuggestedMaxAge.Should().BeGreaterThan(result.SuggestedMinAge,
                $"Failed for genres: {string.Join(",", metadata.Genres ?? [])}");
        }
    }

    [Fact]
    public void SuggestAgeRange_Always_ClampsTo0Through18()
    {
        var extremeCases = new[] { 10, 100, 1000, 100000 };

        foreach (var duration in extremeCases)
        {
            var result = _sut.SuggestAgeRange(
                TestData.CreateAbsMetadata(genres: ["Horror", "Thriller"], isExplicit: true),
                duration, 1);

            result.SuggestedMinAge.Should().BeInRange(0, 18);
            result.SuggestedMaxAge.Should().BeInRange(1, 18);
        }
    }

    [Fact]
    public void SuggestAgeRange_MultipleGenres_CombinesSignals()
    {
        var metadata = TestData.CreateAbsMetadata(
            genres: ["Science Fiction", "Young Adult"]);
        var result = _sut.SuggestAgeRange(metadata, 28800, 20);

        result.Signals.Should().HaveCountGreaterOrEqualTo(2,
            "Multiple matching genres should produce multiple signals");
    }

    [Fact]
    public void SuggestAgeRange_ReasonText_IsNotEmpty()
    {
        var metadata = TestData.CreateAbsMetadata();
        var result = _sut.SuggestAgeRange(metadata, 3600, 10);

        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SuggestAgeRange_NullGenres_DoesNotThrow()
    {
        var metadata = TestData.CreateAbsMetadata(genres: null);
        var act = () => _sut.SuggestAgeRange(metadata, 3600, 10);

        act.Should().NotThrow();
    }

    [Fact]
    public void SuggestAgeRange_NullDescription_DoesNotThrow()
    {
        var metadata = TestData.CreateAbsMetadata(description: null);
        var act = () => _sut.SuggestAgeRange(metadata, 3600, 10);

        act.Should().NotThrow();
    }
}
