using System.Net;
using System.Net.Http.Json;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Infrastructure.Services.Audiobookshelf;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class AudiobookshelfServiceTests
{
    private readonly AudiobookshelfService _sut;
    private readonly FakeHttpMessageHandler _handler;

    public AudiobookshelfServiceTests()
    {
        _handler = new FakeHttpMessageHandler();

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Audiobookshelf"))
            .Returns(() =>
            {
                var client = new HttpClient(_handler);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                return client;
            });

        _sut = new AudiobookshelfService(factory.Object, Mock.Of<ILogger<AudiobookshelfService>>());
    }

    // =========================================================================
    // GetLibraryItemsAsync — query string construction
    // =========================================================================

    [Fact]
    public async Task GetLibraryItemsAsync_WithSearch_AppendsSearchParam()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            search: "harry potter");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("search=harry%20potter");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_WithFilter_AppendsFilterParam()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            filter: "genres.c2NpLWZp");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("filter=genres.c2NpLWZp");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_WithSort_AppendsSortParam()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            sort: "media.metadata.authorName");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("sort=media.metadata.authorName");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_NullSearch_OmitsSearchParam()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            search: null);

        var uri = _handler.LastRequestUri!;
        uri.Should().NotContain("search=");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_EmptySearch_OmitsSearchParam()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            search: "  ");

        var uri = _handler.LastRequestUri!;
        uri.Should().NotContain("search=");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_NullFilter_OmitsFilterParam()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            filter: null);

        var uri = _handler.LastRequestUri!;
        uri.Should().NotContain("filter=");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_SearchWithSpecialChars_UrlEncodes()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            search: "Lord & Rings");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("search=Lord%20%26%20Rings");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_WithCollapseSeries_AppendsBothParams()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            collapseSeries: true, search: "test");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("collapseseries=1");
        uri.Should().Contain("search=test");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_AllParams_BuildsCorrectQueryString()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1",
            page: 2, limit: 10, sort: "media.duration",
            collapseSeries: true, search: "test", filter: "genres.abc");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("page=2");
        uri.Should().Contain("limit=10");
        uri.Should().Contain("sort=media.duration");
        uri.Should().Contain("collapseseries=1");
        uri.Should().Contain("search=test");
        uri.Should().Contain("filter=genres.abc");
        uri.Should().Contain("minified=1");
    }

    [Fact]
    public async Task GetLibraryItemsAsync_DefaultParams_HasMinimalQueryString()
    {
        SetupItemsResponse();

        await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1");

        var uri = _handler.LastRequestUri!;
        uri.Should().Contain("page=0");
        uri.Should().Contain("limit=20");
        uri.Should().Contain("minified=1");
        uri.Should().NotContain("sort=");
        uri.Should().NotContain("search=");
        uri.Should().NotContain("filter=");
        uri.Should().NotContain("collapseseries");
    }

    // =========================================================================
    // GetLibraryItemsAsync — response deserialization
    // =========================================================================

    [Fact]
    public async Task GetLibraryItemsAsync_ValidResponse_DeserializesCorrectly()
    {
        SetupItemsResponse(total: 42);

        var result = await _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1");

        result.Should().NotBeNull();
        result.Total.Should().Be(42);
    }

    [Fact]
    public async Task GetLibraryItemsAsync_ServerError_Throws()
    {
        _handler.SetupResponse(HttpStatusCode.InternalServerError, "Server error");

        var act = () => _sut.GetLibraryItemsAsync("http://abs.local", "token", "lib-1");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // =========================================================================
    // Helper: fake HTTP handler
    // =========================================================================

    private void SetupItemsResponse(int total = 10)
    {
        var body = new AbsLibraryItemsResponse([], total, 20, 0);
        _handler.SetupJsonResponse(body);
    }

    /// <summary>
    /// Minimal HTTP handler that captures the last request URI and returns a canned response.
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _content = "{}";

        public void SetupJsonResponse<T>(T body)
        {
            _statusCode = HttpStatusCode.OK;
            _content = System.Text.Json.JsonSerializer.Serialize(body,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        public void SetupResponse(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.PathAndQuery;

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
