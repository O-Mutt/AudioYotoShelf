using System.Net;
using System.Net.Sockets;
using AudioYotoShelf.Infrastructure.Services.Yoto;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

public class YotoServiceUploadTests
{
    private const string UploadUrl = "https://upload.example.com/presigned";
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task UploadAudioFileAsync_RetriesAfterBrokenPipe_ThenSucceeds()
    {
        var handler = new SequencedHandler(
            SequencedHandler.BrokenPipe(),
            SequencedHandler.BrokenPipe(),
            SequencedHandler.Ok());
        var sut = CreateSut(handler);
        using var stream = new MemoryStream(Payload);

        await sut.UploadAudioFileAsync(UploadUrl, stream, Payload.Length, "audio/mp4");

        handler.CallCount.Should().Be(3);
        // Every attempt read the full payload — proves the stream was rewound between retries.
        handler.BytesReadPerCall.Should().AllBeEquivalentTo(Payload.Length);
    }

    [Fact]
    public async Task UploadAudioFileAsync_ThrowsAfterExhaustingRetries()
    {
        var handler = new SequencedHandler(
            SequencedHandler.BrokenPipe(),
            SequencedHandler.BrokenPipe(),
            SequencedHandler.BrokenPipe(),
            SequencedHandler.BrokenPipe());
        var sut = CreateSut(handler);
        using var stream = new MemoryStream(Payload);

        var act = () => sut.UploadAudioFileAsync(UploadUrl, stream, Payload.Length, "audio/mp4");

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(4); // MaxUploadAttempts
    }

    [Fact]
    public async Task UploadAudioFileAsync_DoesNotRetryOnHttpErrorStatus()
    {
        var handler = new SequencedHandler(SequencedHandler.Status(HttpStatusCode.Forbidden));
        var sut = CreateSut(handler);
        using var stream = new MemoryStream(Payload);

        var act = () => sut.UploadAudioFileAsync(UploadUrl, stream, Payload.Length, "audio/mp4");

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(1); // a real status is a hard failure, not a transient reset
    }

    [Fact]
    public void GetAuthorizationUrl_UsesConfiguredAuthBase()
    {
        // Lets E2E/tests point the OAuth flow at a mock Yoto server instead of login.yotoplay.com.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Yoto:ClientId"] = "client-123",
                ["Yoto:AuthBase"] = "http://localhost:9999",
            })
            .Build();
        var sut = new YotoService(Mock.Of<IHttpClientFactory>(), config, Mock.Of<ILogger<YotoService>>());

        var url = sut.GetAuthorizationUrl("http://app/callback", "state-1");

        url.Should().StartWith("http://localhost:9999/authorize");
        url.Should().Contain("client_id=client-123");
    }

    private static TestableYotoService CreateSut(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("YotoUpload"))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new TestableYotoService(factory.Object);
    }

    // Overrides the backoff to a no-op so retry tests run instantly.
    private sealed class TestableYotoService(IHttpClientFactory factory)
        : YotoService(factory, new ConfigurationBuilder().Build(),
            Mock.Of<ILogger<YotoService>>())
    {
        protected override Task DelayBetweenUploadAttemptsAsync(int attempt, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class SequencedHandler(params Func<byte[], HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        private int _index;
        public int CallCount { get; private set; }
        public List<int> BytesReadPerCall { get; } = [];

        public static Func<byte[], HttpResponseMessage> Ok() =>
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        public static Func<byte[], HttpResponseMessage> Status(HttpStatusCode code) =>
            _ => new HttpResponseMessage(code);

        public static Func<byte[], HttpResponseMessage> BrokenPipe() => _ =>
            throw new HttpRequestException(
                "Error while copying content to a stream.",
                new IOException(
                    "Unable to write data to the transport connection: Broken pipe.",
                    new SocketException((int)SocketError.ConnectionReset)));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            // Consume the body (advancing the source stream to its end) before acting, so a
            // subsequent attempt only succeeds if the production code rewound the stream.
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            BytesReadPerCall.Add(body.Length);

            var step = steps[Math.Min(_index++, steps.Length - 1)];
            return step(body);
        }
    }
}
