using AudioYotoShelf.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class TempFileSweeperTests : IDisposable
{
    private const string Hex = "0123456789abcdef0123456789abcdef";
    private const string TransferId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private readonly string _transferDir = Directory.CreateTempSubdirectory("ays-sweep-transfer-").FullName;
    private readonly string _systemTemp = Directory.CreateTempSubdirectory("ays-sweep-tmp-").FullName;
    private readonly Mock<ILogger> _logger = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public void Dispose()
    {
        Directory.Delete(_transferDir, recursive: true);
        Directory.Delete(_systemTemp, recursive: true);
    }

    /// <summary>Creates a file last written an hour before the sweep starts.</summary>
    private string Leftover(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[1024]);
        File.SetLastWriteTimeUtc(path, _startedAt.AddHours(-1));
        return path;
    }

    private int Sweep() => TempFileSweeper.Sweep([_transferDir, _systemTemp], _startedAt, _logger.Object);

    [Theory]
    [InlineData($"pl_{Hex}.m4b")]
    [InlineData($"pl_{Hex}")]
    [InlineData($"{TransferId}_{Hex}_input.m4b")]
    [InlineData($"{TransferId}_{Hex}_input")]
    [InlineData($"{TransferId}_{Hex}_track12.tmp")]
    public void DeletesTransferDownloadsLeftInTheTransferFolder(string name)
    {
        var path = Leftover(_transferDir, name);

        Sweep().Should().Be(1);

        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData($"chapter_{Hex}.m4a")]
    [InlineData($"audio_{Hex}.m4a")]
    [InlineData($"segment_{Hex}_007.m4a")]
    [InlineData($"concat_{Hex}.txt")]
    public void DeletesFfmpegOutputLeftInTheSystemTempFolder(string name)
    {
        var path = Leftover(_systemTemp, name);

        Sweep().Should().Be(1);

        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("audio_notes.m4a")]                        // looks like ours, but not our name
    [InlineData($"audio_{Hex}")]                           // ffmpeg output always has an extension
    [InlineData($"segment_{Hex}_7.m4a")]
    [InlineData($"pl_{Hex}.m4b.bak")]
    [InlineData("holiday.mp3")]
    [InlineData($"x{TransferId}_{Hex}_input.m4b")]
    [InlineData($"{TransferId}_{Hex}_track.tmp")]
    public void LeavesFilesThatAreNotTheAppsOwn(string name)
    {
        var inTransferDir = Leftover(_transferDir, name);
        var inSystemTemp = Leftover(_systemTemp, name);

        Sweep().Should().Be(0);

        File.Exists(inTransferDir).Should().BeTrue();
        File.Exists(inSystemTemp).Should().BeTrue();
    }

    [Fact]
    public void LeavesAFileWrittenAfterTheSweepStarted()
    {
        var path = Path.Combine(_transferDir, $"pl_{Hex}.m4b");
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, _startedAt.AddSeconds(1));

        Sweep().Should().Be(0);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void LeavesFoldersAndWhatIsInThem()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_systemTemp, $"audio_{Hex}.m4a")).FullName;
        var inside = Leftover(_systemTemp, Path.Combine(folder, $"audio_{Hex}.m4a"));

        Sweep().Should().Be(0);

        File.Exists(inside).Should().BeTrue();
    }

    [Fact]
    public void AFolderListedTwice_IsSweptOnce()
    {
        // The Docker image points TMPDIR at the transfer folder, so both entries name one folder.
        Leftover(_transferDir, $"audio_{Hex}.m4a");
        Leftover(_transferDir, $"pl_{Hex}.mp3");

        TempFileSweeper.Sweep([_transferDir, _transferDir + Path.DirectorySeparatorChar], _startedAt, _logger.Object)
            .Should().Be(2);
    }

    [Fact]
    public void AMissingFolder_IsSkipped()
    {
        Leftover(_systemTemp, $"concat_{Hex}.txt");

        var act = () => TempFileSweeper.Sweep(
            [Path.Combine(_transferDir, "does-not-exist"), _systemTemp], _startedAt, _logger.Object);

        act.Should().NotThrow().Which.Should().Be(1);
    }
}
