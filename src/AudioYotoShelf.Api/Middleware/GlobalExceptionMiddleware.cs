using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AudioYotoShelf.Api.Middleware;

/// <summary>
/// Global exception handling middleware that converts unhandled exceptions
/// to RFC 7807 ProblemDetails responses with structured logging.
/// </summary>
public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail) = exception switch
        {
            InvalidOperationException ex => (HttpStatusCode.BadRequest, "Invalid Operation", ex.Message),
            UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, "Unauthorized", ex.Message),
            KeyNotFoundException ex => (HttpStatusCode.NotFound, "Not Found", ex.Message),
            TimeoutException ex => (HttpStatusCode.GatewayTimeout, "Timeout", ex.Message),
            OperationCanceledException => (HttpStatusCode.BadRequest, "Cancelled", "The operation was cancelled"),
            HttpRequestException ex => (HttpStatusCode.BadGateway, "External Service Error", ex.Message),
            FluentValidation.ValidationException ex => (HttpStatusCode.UnprocessableEntity, "Validation Error",
                string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))),
            _ => (HttpStatusCode.InternalServerError, "Internal Server Error", "An unexpected error occurred")
        };

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.com/{(int)statusCode}"
        };

        // Add correlation ID for tracing
        var traceId = context.TraceIdentifier;
        problemDetails.Extensions["traceId"] = traceId;

        // Structured logging
        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path} [TraceId: {TraceId}]",
                context.Request.Method, context.Request.Path, traceId);
        }
        else
        {
            logger.LogWarning(exception, "Handled exception on {Method} {Path}: {StatusCode} {Title} [TraceId: {TraceId}]",
                context.Request.Method, context.Request.Path, (int)statusCode, title, traceId);
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }
}

/// <summary>
/// Extension method to register the middleware in the pipeline.
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
