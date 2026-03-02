using System.Net;
using System.Text.Json;
using AudioYotoShelf.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class GlobalExceptionMiddlewareTests
{
    private readonly Mock<ILogger<GlobalExceptionMiddleware>> _logger = new();

    private GlobalExceptionMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, _logger.Object);

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";
        return context;
    }

    private static async Task<ProblemDetails?> ReadProblemDetails(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // =========================================================================
    // Status code mapping
    // =========================================================================

    [Fact]
    public async Task InvalidOperationException_Returns400()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Bad input"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemDetails(context);
        problem!.Title.Should().Be("Invalid Operation");
        problem.Detail.Should().Be("Bad input");
    }

    [Fact]
    public async Task UnauthorizedAccessException_Returns401()
    {
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException("No auth"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
        var problem = await ReadProblemDetails(context);
        problem!.Title.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task KeyNotFoundException_Returns404()
    {
        var middleware = CreateMiddleware(_ => throw new KeyNotFoundException("Not found"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task TimeoutException_Returns504()
    {
        var middleware = CreateMiddleware(_ => throw new TimeoutException("Timed out"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(504);
    }

    [Fact]
    public async Task HttpRequestException_Returns502()
    {
        var middleware = CreateMiddleware(_ => throw new HttpRequestException("External failure"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(502);
        var problem = await ReadProblemDetails(context);
        problem!.Title.Should().Be("External Service Error");
    }

    [Fact]
    public async Task OperationCanceledException_Returns400()
    {
        var middleware = CreateMiddleware(_ => throw new OperationCanceledException());
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UnhandledException_Returns500()
    {
        var middleware = CreateMiddleware(_ => throw new NullReferenceException("Oops"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
        var problem = await ReadProblemDetails(context);
        problem!.Title.Should().Be("Internal Server Error");
        problem.Detail.Should().Be("An unexpected error occurred");
    }

    // =========================================================================
    // Response format
    // =========================================================================

    [Fact]
    public async Task AllExceptions_ReturnProblemJson_ContentType()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("boom"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task AllExceptions_IncludeTraceId()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("boom"));
        var context = CreateHttpContext();
        context.TraceIdentifier = "trace-abc-123";

        await middleware.InvokeAsync(context);

        var problem = await ReadProblemDetails(context);
        problem!.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task AllExceptions_IncludeInstancePath()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("boom"));
        var context = CreateHttpContext();
        context.Request.Path = "/api/transfers/123";

        await middleware.InvokeAsync(context);

        var problem = await ReadProblemDetails(context);
        problem!.Instance.Should().Be("/api/transfers/123");
    }

    // =========================================================================
    // Logging behavior
    // =========================================================================

    [Fact]
    public async Task InternalServerError_LogsError()
    {
        var middleware = CreateMiddleware(_ => throw new NullReferenceException("Oops"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        _logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ClientError_LogsWarning()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("bad"));
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        _logger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // =========================================================================
    // No exception path
    // =========================================================================

    [Fact]
    public async Task NoException_PassesThrough()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }
}
