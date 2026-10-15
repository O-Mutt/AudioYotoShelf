using AudioYotoShelf.Infrastructure.Services.IconGeneration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;

namespace AudioYotoShelf.Infrastructure.Tests;

public class GeminiIconGenerationServiceTests
{
    private readonly GeminiIconGenerationService _sut;

    public GeminiIconGenerationServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "test-key",
                ["Gemini:Model"] = "gemini-3.1-flash-image"
            })
            .Build();

        var cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        _sut = new GeminiIconGenerationService(
            Mock.Of<IHttpClientFactory>(),
            cache,
            config,
            Mock.Of<ILogger<GeminiIconGenerationService>>());
    }

    // =========================================================================
    // BuildChapterIconPrompt
    // =========================================================================

    [Fact]
    public void BuildChapterIconPrompt_IncludesChapterTitle()
    {
        var prompt = _sut.BuildChapterIconPrompt("The Dark Forest", "Three Body Problem", "Science Fiction");
        prompt.Should().Contain("The Dark Forest");
    }

    [Fact]
    public void BuildChapterIconPrompt_IncludesBookTitle()
    {
        var prompt = _sut.BuildChapterIconPrompt("Chapter 1", "Harry Potter", null);
        prompt.Should().Contain("Harry Potter");
    }

    [Fact]
    public void BuildChapterIconPrompt_IncludesGenreWhenProvided()
    {
        var prompt = _sut.BuildChapterIconPrompt("Chapter 1", "Test", "Fantasy");
        prompt.Should().Contain("Fantasy");
    }

    [Fact]
    public void BuildChapterIconPrompt_OmitsGenreWhenNull()
    {
        var prompt = _sut.BuildChapterIconPrompt("Chapter 1", "Test", null);
        prompt.Should().NotContain("genre is");
    }

    [Fact]
    public void BuildChapterIconPrompt_IncludesPixelArtInstructions()
    {
        var prompt = _sut.BuildChapterIconPrompt("Chapter 1", "Test", null);
        prompt.Should().Contain("16x16");
        prompt.Should().Contain("pixel art");
        prompt.Should().Contain("No text");
        prompt.Should().Contain("No black");
    }

    [Fact]
    public void BuildChapterIconPrompt_WarnsAboutBlackPixels()
    {
        var prompt = _sut.BuildChapterIconPrompt("Chapter 1", "Test", null);
        prompt.Should().Contain("black")
            .And.Contain("LED");
    }

    // =========================================================================
    // SearchPublicIconsAsync (in-memory cache behavior)
    // =========================================================================

    [Fact]
    public async Task SearchPublicIconsAsync_EmptyCache_ReturnsEmpty()
    {
        var result = await _sut.SearchPublicIconsAsync("dragon");
        result.Should().BeEmpty();
    }

    // =========================================================================
    // ConvertCoverToIconAsync
    // =========================================================================

    [Fact]
    public async Task ConvertCoverToIconAsync_ValidImage_Returns16x16Bytes()
    {
        // Create a simple 4x4 red PNG for testing
        var pngBytes = CreateMinimalPng(4, 4);
        using var stream = new MemoryStream(pngBytes);

        var result = await _sut.ConvertCoverToIconAsync(stream);

        result.Should().NotBeEmpty();
        // Verify it's a valid PNG (starts with PNG magic bytes)
        result[0].Should().Be(0x89);
        result[1].Should().Be(0x50); // 'P'
        result[2].Should().Be(0x4E); // 'N'
        result[3].Should().Be(0x47); // 'G'
    }

    [Fact]
    public async Task ConvertCoverToIconAsync_ResizesTo16x16AndReplacesPureBlack()
    {
        using var stream = new MemoryStream(CreateMinimalPng(4, 4, SKColors.Black));

        var result = await _sut.ConvertCoverToIconAsync(stream);

        using var icon = SKBitmap.Decode(result);
        icon.Width.Should().Be(16);
        icon.Height.Should().Be(16);
        icon.Pixels.Should().OnlyContain(p => p == new SKColor(8, 8, 8, 255));
    }

    [Fact]
    public async Task ConvertCoverToIconAsync_InvalidImage_Throws()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);

        var act = () => _sut.ConvertCoverToIconAsync(stream);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static byte[] CreateMinimalPng(int width, int height, SKColor? fill = null)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(fill ?? new SKColor(200, 50, 50, 255));
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
