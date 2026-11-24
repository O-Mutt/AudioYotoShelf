using AudioYotoShelf.Core.Configuration;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class YotoCardLimitsTests
{
    [Fact]
    public void Validate_Defaults_Pass()
    {
        var act = () => new YotoCardLimits().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ADurationCapThatIsSet_Passes()
    {
        var act = () => new YotoCardLimits { MaxTotalDurationSeconds = 18000 }.Validate();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_ABitrateOfZeroOrLess_Fails(int kbps)
    {
        var act = () => new YotoCardLimits { TranscodedBitrateKbps = kbps }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*TranscodedBitrateKbps must be greater than 0*");
    }

    [Fact]
    public void Validate_AZeroDurationCap_Fails()
    {
        var act = () => new YotoCardLimits { MaxTotalDurationSeconds = 0 }.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxTotalDurationSeconds must be unset or greater than 0*");
    }

    [Fact]
    public void Validate_ListsEveryProblem()
    {
        var act = () => new YotoCardLimits
        {
            MaxTracks = 0,
            MaxTrackBytes = 0,
            MaxTrackDurationSeconds = 0,
            MaxTotalBytes = 0,
            TranscodedBitrateKbps = 0,
        }.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid Yoto:CardLimits settings: *TranscodedBitrateKbps*MaxTracks*MaxTrackBytes*MaxTrackDurationSeconds*MaxTotalBytes*");
    }
}
