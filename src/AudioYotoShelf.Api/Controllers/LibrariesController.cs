using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibrariesController(
    IAudiobookshelfService absService,
    IAgeSuggestionService ageSuggestionService,
    AudioYotoShelfDbContext db,
    ILogger<LibrariesController> logger) : ControllerBase
{
    /// <summary>
    /// Get all accessible libraries for the current user.
    /// </summary>
    [HttpGet("{userConnectionId:guid}")]
    public async Task<IActionResult> GetLibraries(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return Unauthorized("No valid Audiobookshelf connection");

        var libraries = await absService.GetLibrariesAsync(user.AudiobookshelfUrl, user.AudiobookshelfToken!, ct);

        // Filter to book-type libraries only (exclude podcasts)
        var bookLibraries = libraries.Where(l => l.MediaType == "book").ToArray();
        return Ok(bookLibraries);
    }

    /// <summary>
    /// Get paginated books in a library.
    /// </summary>
    [HttpGet("{userConnectionId:guid}/library/{libraryId}/items")]
    public async Task<IActionResult> GetLibraryItems(
        Guid userConnectionId, string libraryId,
        [FromQuery] int page = 0, [FromQuery] int limit = 20,
        [FromQuery] bool collapseSeries = false,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] bool sortDesc = false,
        [FromQuery] string? filter = null,
        CancellationToken ct = default)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return Unauthorized("No valid Audiobookshelf connection");

        // Build sort string: ABS uses "field" for asc, prepend "-" is not supported natively,
        // but ABS API uses a separate desc param. We'll build the sort value.
        var sortParam = sort ?? "media.metadata.title";

        var items = await absService.GetLibraryItemsAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId,
            page, limit, sortParam, collapseSeries, search, filter, ct);

        return Ok(items);
    }

    /// <summary>
    /// Get detailed book info including chapters, with age suggestion.
    /// </summary>
    [HttpGet("{userConnectionId:guid}/items/{itemId}")]
    public async Task<IActionResult> GetItem(Guid userConnectionId, string itemId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return Unauthorized("No valid Audiobookshelf connection");

        var item = await absService.GetLibraryItemAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, ct);

        // Calculate age suggestion
        var ageSuggestion = item.Media?.Metadata is not null
            ? ageSuggestionService.SuggestAgeRange(
                item.Media.Metadata,
                item.Media.Duration,
                item.Media.NumChapters)
            : null;

        // Check if this book has already been transferred
        var existingTransfer = await db.CardTransfers
            .Where(t => t.UserConnectionId == userConnectionId && t.AbsLibraryItemId == itemId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            Item = item,
            AgeSuggestion = ageSuggestion,
            ExistingTransfer = existingTransfer is not null ? new
            {
                existingTransfer.Id,
                existingTransfer.Status,
                existingTransfer.YotoCardId,
                existingTransfer.CompletedAt
            } : null
        });
    }

    /// <summary>
    /// Get series list for a library.
    /// </summary>
    [HttpGet("{userConnectionId:guid}/library/{libraryId}/series")]
    public async Task<IActionResult> GetSeries(
        Guid userConnectionId, string libraryId,
        [FromQuery] int page = 0, [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return Unauthorized("No valid Audiobookshelf connection");

        var series = await absService.GetSeriesAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId, page, limit, ct);

        return Ok(series);
    }

    /// <summary>
    /// Get series detail with all books.
    /// </summary>
    [HttpGet("{userConnectionId:guid}/series/{seriesId}")]
    public async Task<IActionResult> GetSeriesDetail(Guid userConnectionId, string seriesId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return Unauthorized("No valid Audiobookshelf connection");

        var seriesDetail = await absService.GetSeriesDetailAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, seriesId, ct);

        return Ok(seriesDetail);
    }

    /// <summary>
    /// Proxy cover image from ABS (avoids CORS issues).
    /// </summary>
    [HttpGet("{userConnectionId:guid}/items/{itemId}/cover")]
    public async Task<IActionResult> GetCover(Guid userConnectionId, string itemId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return Unauthorized("No valid Audiobookshelf connection");

        var coverStream = await absService.GetCoverImageAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, ct);

        return File(coverStream, "image/jpeg");
    }
}
