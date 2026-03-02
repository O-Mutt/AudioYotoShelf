using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Infrastructure.Caching;
using AudioYotoShelf.Infrastructure.Services.IconGeneration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class RateLimitedIconServiceTests
{
    private readonly Mock<GeminiIconGenerationService> _innerMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly RateLimitedIconService _sut;

    public RateLimitedIconServiceTests()
    {
        // GeminiIconGenerationService is a concrete class; we mock its virtual methods indirectly
        // by using a mock of ICacheService to control rate limit state
        _innerMock = new Mock<GeminiIconGenerationService>(
            MockBehavior.Loose,
            Mock.Of<System.Net.Http.IHttpClientFactory>(),
            Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>(),
            Mock.Of<ILogger<GeminiIconGenerationService>>());

        _cacheMock = new Mock<ICacheService>();

        _sut = new RateLimitedIconService(
            _innerMock.Object,
            _cacheMock.Object,
            Mock.Of<ILogger<RateLimitedIconService>>());
    }

    [Fact]
    public async Task UnderLimit_DelegatesToInner()
    {
        SetupCount(100);

        _innerMock.Setup(s => s.GenerateIconAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });

        var result = await _sut.GenerateIconAsync("test prompt");

        result.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task AtLimit_ThrowsWithFallbackMessage()
    {
        SetupCount(RateLimitedIconService.DailyLimit);

        var act = () => _sut.GenerateIconAsync("test prompt");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*daily*limit*");
    }

    [Fact]
    public async Task GenerateChapterIcon_AtLimit_FallsToPublicIcons()
    {
        SetupCount(RateLimitedIconService.DailyLimit);

        _innerMock.Setup(s => s.SearchPublicIconsAsync("forest", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new YotoPublicIcon("icon-1", "Forest", ["nature", "trees"], "https://icons.yoto.com/forest.png")
            ]);

        var act = () => _sut.GenerateChapterIconAsync("Into the Forest", "Adventure Book", "fantasy");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Public icon available*");
    }

    [Fact]
    public async Task GenerateChapterIcon_AtLimit_NoPublicIcons_SuggestsCoverConversion()
    {
        SetupCount(RateLimitedIconService.DailyLimit);

        _innerMock.Setup(s => s.SearchPublicIconsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var act = () => _sut.GenerateChapterIconAsync("Chapter 1", "Unknown Book", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ConvertCoverToIconAsync*");
    }

    [Fact]
    public async Task GenerateIcon_IncrementsCounter()
    {
        SetupCount(0);

        _innerMock.Setup(s => s.GenerateIconAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1 });

        await _sut.GenerateIconAsync("test");

        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k.StartsWith("gemini:count:")),
            It.IsAny<RateLimitedIconService.CountWrapper>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConvertCoverToIcon_AlwaysPassesThrough()
    {
        SetupCount(RateLimitedIconService.DailyLimit); // Over limit

        var stream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG magic bytes
        _innerMock.Setup(s => s.ConvertCoverToIconAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2 });

        var result = await _sut.ConvertCoverToIconAsync(stream);

        result.Should().BeEquivalentTo(new byte[] { 1, 2 });
    }

    [Fact]
    public async Task SearchPublicIcons_AlwaysPassesThrough()
    {
        SetupCount(RateLimitedIconService.DailyLimit);

        _innerMock.Setup(s => s.SearchPublicIconsAsync("cat", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new YotoPublicIcon("1", "Cat", ["animal"], "url")]);

        var result = await _sut.SearchPublicIconsAsync("cat", 5);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void BuildChapterIconPrompt_AlwaysPassesThrough()
    {
        _innerMock.Setup(s => s.BuildChapterIconPrompt("ch", "book", "genre"))
            .Returns("test prompt");

        var result = _sut.BuildChapterIconPrompt("ch", "book", "genre");

        result.Should().Be("test prompt");
    }

    [Fact]
    public async Task IsOverLimit_ExactlyAtLimit_ReturnsTrue()
    {
        SetupCount(RateLimitedIconService.DailyLimit);

        var result = await _sut.IsOverLimitAsync(CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOverLimit_BelowLimit_ReturnsFalse()
    {
        SetupCount(RateLimitedIconService.DailyLimit - 1);

        var result = await _sut.IsOverLimitAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsOverLimit_NoCacheEntry_ReturnsFalse()
    {
        _cacheMock.Setup(c => c.GetAsync<RateLimitedIconService.CountWrapper>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RateLimitedIconService.CountWrapper?)null);

        var result = await _sut.IsOverLimitAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    // Helper

    private void SetupCount(int count)
    {
        _cacheMock.Setup(c => c.GetAsync<RateLimitedIconService.CountWrapper>(
                It.Is<string>(k => k.StartsWith("gemini:count:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitedIconService.CountWrapper(count));
    }
}
