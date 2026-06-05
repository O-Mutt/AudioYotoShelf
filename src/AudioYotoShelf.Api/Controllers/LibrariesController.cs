using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibrariesController(
    IAudiobookshelfService absService,
    IAgeSuggestionService ageSuggestionService,
    AudioYotoShelfDbContext db,
    ILogger<LibrariesController> logger) : AppControllerBase
{
    /// <summary>
    /// Loads the authenticated caller's connection, verifies it has a usable Audiobookshelf
    /// connection, and refreshes the stored access token from the refresh token when it's near
    /// expiry. Returns null when there is no valid connection (callers translate that to 401).
    /// </summary>
    private async Task<UserConnection?> ResolveAbsUserAsync(CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([CurrentUserConnectionId], ct);
        if (user is null || !user.HasValidAbsConnection)
            return null;

        try
        {
            await AbsTokens.EnsureValidAsync(db, absService, user, logger, ct);
        }
        catch (HttpRequestException ex)
        {
            // The stored refresh token is expired/revoked — treat as "no valid connection" (→ 401
            // so the client re-authenticates) rather than letting it surface as a 502 that looks
            // like Audiobookshelf itself is down.
            logger.LogWarning(ex, "Audiobookshelf token refresh failed for {Username}", user.Username);
            return null;
        }
        return user;
    }

    /// <summary>
    /// Get all accessible libraries for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLibraries(CancellationToken ct)
    {
        var user = await ResolveAbsUserAsync(ct);
        if (user is null)
            return Unauthorized("No valid Audiobookshelf connection");

        var libraries = await absService.GetLibrariesAsync(user.AudiobookshelfUrl, user.AudiobookshelfToken!, ct);

        // Filter to book-type libraries only (exclude podcasts)
        var bookLibraries = libraries.Where(l => l.MediaType == "book").ToArray();
        return Ok(bookLibraries);
    }

    /// <summary>
    /// Get paginated books in a library.
    /// </summary>
    [HttpGet("library/{libraryId}/items")]
    public async Task<IActionResult> GetLibraryItems(
        string libraryId,
        [FromQuery] int page = 0, [FromQuery] int limit = 20,
        [FromQuery] bool collapseSeries = false,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] bool sortDesc = false,
        [FromQuery] string? filter = null,
        CancellationToken ct = default)
    {
        var user = await ResolveAbsUserAsync(ct);
        if (user is null)
            return Unauthorized("No valid Audiobookshelf connection");

        // Free-text search uses the dedicated ABS search endpoint; the items endpoint ignores it.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var matches = await absService.SearchLibraryItemsAsync(
                user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId, search, limit, ct);
            return Ok(new AbsLibraryItemsResponse(matches, matches.Length, limit, 0));
        }

        var sortParam = sort ?? "media.metadata.title";
        var items = await absService.GetLibraryItemsAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId,
            page, limit, sortParam, sortDesc, collapseSeries, search, filter, ct);

        return Ok(items);
    }

    /// <summary>
    /// Get detailed book info including chapters, with age suggestion.
    /// </summary>
    [HttpGet("items/{itemId}")]
    public async Task<IActionResult> GetItem(string itemId, CancellationToken ct)
    {
        var user = await ResolveAbsUserAsync(ct);
        if (user is null)
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
            .Where(t => t.UserConnectionId == user.Id && t.AbsLibraryItemId == itemId)
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
    [HttpGet("library/{libraryId}/series")]
    public async Task<IActionResult> GetSeries(
        string libraryId,
        [FromQuery] int page = 0, [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var user = await ResolveAbsUserAsync(ct);
        if (user is null)
            return Unauthorized("No valid Audiobookshelf connection");

        var series = await absService.GetSeriesAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId, page, limit, ct);

        return Ok(series);
    }

    /// <summary>
    /// Get series detail with all books.
    /// </summary>
    [HttpGet("series/{seriesId}")]
    public async Task<IActionResult> GetSeriesDetail(
        string seriesId, [FromQuery] string? libraryId, CancellationToken ct)
    {
        var user = await ResolveAbsUserAsync(ct);
        if (user is null)
            return Unauthorized("No valid Audiobookshelf connection");

        // The series' books are fetched from the library items endpoint, which needs a library.
        // When the caller specifies one, use it. Otherwise search every book library the user
        // has — the series may live in any of them (multi-library homelabs, deep links).
        if (!string.IsNullOrEmpty(libraryId))
        {
            var detail = await absService.GetSeriesDetailAsync(
                user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId, seriesId, ct);
            return Ok(detail);
        }

        var libraries = await absService.GetLibrariesAsync(user.AudiobookshelfUrl, user.AudiobookshelfToken!, ct);
        var bookLibraries = libraries.Where(l => l.MediaType == "book").ToArray();

        AbsSeriesItem? resolved = null;
        foreach (var library in bookLibraries)
        {
            var detail = await absService.GetSeriesDetailAsync(
                user.AudiobookshelfUrl, user.AudiobookshelfToken!, library.Id, seriesId, ct);
            resolved ??= detail;                 // keep first result as a fallback (name/description)
            if (detail.Books.Length > 0)         // the library that actually holds the series
            {
                resolved = detail;
                break;
            }
        }

        return resolved is null ? NotFound() : Ok(resolved);
    }

    /// <summary>
    /// Proxy cover image from ABS (avoids CORS issues).
    /// </summary>
    [HttpGet("items/{itemId}/cover")]
    public async Task<IActionResult> GetCover(string itemId, CancellationToken ct)
    {
        var user = await ResolveAbsUserAsync(ct);
        if (user is null)
            return Unauthorized("No valid Audiobookshelf connection");

        var coverStream = await absService.GetCoverImageAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, ct);

        return File(coverStream, "image/jpeg");
    }
}
