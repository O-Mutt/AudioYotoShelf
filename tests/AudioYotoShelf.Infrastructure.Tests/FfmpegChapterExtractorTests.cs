using AudioYotoShelf.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class FfmpegChapterExtractorTests
{
    private readonly FfmpegChapterExtractor _sut;

    public FfmpegChapterExtractorTests()
    {
        _sut = new FfmpegChapterExtractor(Mock.Of<ILogger<FfmpegChapterExtractor>>());
    }

    [Fact]
    public async Task IsFfmpegAvailableAsync_ReturnsBoolean()
    {
        // This test verifies the method doesn't throw and returns a definitive answer.
        // On CI without FFmpeg it returns false; locally with FFmpeg it returns true.
        var result = await _sut.IsFfmpegAvailableAsync();
        result.Should().BeOneOf(true, false);
    }

    [Fact]
    public async Task ExtractChapterAsync_NullInputPath_Throws()
    {
        var act = () => _sut.ExtractChapterAsync(null!, 0, 300, "m4a");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExtractChapterAsync_NonExistentFile_Throws()
    {
        var act = () => _sut.ExtractChapterAsync("/nonexistent/file.m4b", 0, 300, "m4a");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ExtractChapterAsync_NegativeStartTime_Throws()
    {
        // Create a temp file so the file-exists check passes
        var tempFile = Path.GetTempFileName();
        try
        {
            var act = () => _sut.ExtractChapterAsync(tempFile, -5, 300, "m4a");
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractChapterAsync_EndBeforeStart_Throws()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var act = () => _sut.ExtractChapterAsync(tempFile, 300, 100, "m4a");
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
