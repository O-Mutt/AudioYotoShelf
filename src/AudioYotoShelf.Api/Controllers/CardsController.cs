using System.Net;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CardsController(
    IYotoService yotoService,
    AudioYotoShelfDbContext db,
    ILogger<CardsController> logger) : ControllerBase
{
    /// <summary>
    /// List user's Yoto MYO cards.
    /// Tries the Yoto family library endpoint first; if it returns 403 (scope/permission
    /// mismatch is common), falls back to fetching individual card details for each card
    /// we transferred via this app.
    /// </summary>
    [HttpGet("{userConnectionId:guid}")]
    public async Task<IActionResult> GetCards(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);

        // Require tokens exist, but don't reject on expiry — EnsureYotoTokenAsync will refresh
        if (user is null || string.IsNullOrEmpty(user.YotoRefreshToken))
            return Unauthorized("No valid Yoto connection");

        string accessToken;
        try
        {
            accessToken = await EnsureYotoTokenAsync(user, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Yoto token refresh failed for user {Username}", user.Username);
            return Unauthorized(new { Message = "Your Yoto session has expired. Please reconnect in Settings." });
        }

        // Load our transfer records regardless — used for enrichment and as fallback
        var knownCards = await db.CardTransfers
            .Where(t => t.UserConnectionId == userConnectionId && t.YotoCardId != null)
            .Select(t => new { t.YotoCardId, t.BookTitle, t.BookAuthor })
            .ToListAsync(ct);

        var knownLookup = knownCards
            .DistinctBy(t => t.YotoCardId)
            .ToDictionary(t => t.YotoCardId!);

        // Attempt 1: full Yoto family library list
        YotoCard[]? allCards = null;
        try
        {
            allCards = await yotoService.GetUserCardsAsync(accessToken, ct);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                "Yoto family library returned {Status} for user {Username} — falling back to per-card fetch",
                ex.StatusCode, user.Username);
        }

        if (allCards is not null)
        {
            var enriched = allCards.Select(card => new
            {
                card.CardId,
                card.Metadata,
                ChapterCount = card.Content?.Chapters?.Length ?? 0,
                TrackCount = card.Content?.Chapters?.Sum(ch => ch.Tracks?.Length ?? 0) ?? 0,
                FromAudioYotoShelf = knownLookup.ContainsKey(card.CardId),
                SourceBookTitle = knownLookup.TryGetValue(card.CardId, out var tx) ? tx.BookTitle : null,
                SourceBookAuthor = knownLookup.TryGetValue(card.CardId, out var tx2) ? tx2.BookAuthor : null,
            }).ToArray();

            return Ok(enriched);
        }

        // Fallback: fetch individual card details for each card we created
        var fallback = new List<object>();
        foreach (var entry in knownLookup.Values)
        {
            try
            {
                var card = await yotoService.GetCardContentAsync(accessToken, entry.YotoCardId!, ct);
                fallback.Add(new
                {
                    card.CardId,
                    card.Metadata,
                    ChapterCount = card.Content?.Chapters?.Length ?? 0,
                    TrackCount = card.Content?.Chapters?.Sum(ch => ch.Tracks?.Length ?? 0) ?? 0,
                    FromAudioYotoShelf = true,
                    SourceBookTitle = entry.BookTitle,
                    SourceBookAuthor = entry.BookAuthor,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not fetch details for Yoto card {CardId}", entry.YotoCardId);
            }
        }

        logger.LogInformation(
            "Cards fallback: returned {Count} cards from transfer history for user {Username}",
            fallback.Count, user.Username);

        return Ok(fallback.ToArray());
    }

    /// <summary>
    /// Get full card content including chapters and tracks.
    /// </summary>
    [HttpGet("{userConnectionId:guid}/{cardId}")]
    public async Task<IActionResult> GetCard(Guid userConnectionId, string cardId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || string.IsNullOrEmpty(user.YotoRefreshToken))
            return Unauthorized("No valid Yoto connection");

        string accessToken;
        try { accessToken = await EnsureYotoTokenAsync(user, ct); }
        catch { return Unauthorized(new { Message = "Your Yoto session has expired. Please reconnect in Settings." }); }

        var card = await yotoService.GetCardContentAsync(accessToken, cardId, ct);
        return Ok(card);
    }

    /// <summary>
    /// Delete a Yoto MYO card.
    /// </summary>
    [HttpDelete("{userConnectionId:guid}/{cardId}")]
    public async Task<IActionResult> DeleteCard(Guid userConnectionId, string cardId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || string.IsNullOrEmpty(user.YotoRefreshToken))
            return Unauthorized("No valid Yoto connection");

        string accessToken;
        try { accessToken = await EnsureYotoTokenAsync(user, ct); }
        catch { return Unauthorized(new { Message = "Your Yoto session has expired. Please reconnect in Settings." }); }

        await yotoService.DeleteCardAsync(accessToken, cardId, ct);

        logger.LogInformation("Deleted Yoto card {CardId} for user {Username}", cardId, user.Username);

        return Ok(new { Message = "Card deleted", CardId = cardId });
    }

    /// <summary>
    /// Refresh if expiry is unknown or within 5 minutes.
    /// </summary>
    private async Task<string> EnsureYotoTokenAsync(UserConnection user, CancellationToken ct)
    {
        if (!user.YotoTokenExpiresAt.HasValue ||
            user.YotoTokenExpiresAt.Value < DateTimeOffset.UtcNow.AddMinutes(5))
        {
            logger.LogInformation("Refreshing Yoto token for user {Username} (expiry: {Expiry})",
                user.Username, user.YotoTokenExpiresAt?.ToString() ?? "null");
            return await ForceRefreshYotoTokenAsync(user, ct);
        }

        return user.YotoAccessToken!;
    }

    private async Task<string> ForceRefreshYotoTokenAsync(UserConnection user, CancellationToken ct)
    {
        var newToken = await yotoService.RefreshTokenAsync(user.YotoRefreshToken!, ct);
        user.YotoAccessToken = newToken.AccessToken;
        user.YotoRefreshToken = newToken.RefreshToken ?? user.YotoRefreshToken;
        user.YotoTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);
        await db.SaveChangesAsync(ct);
        return newToken.AccessToken;
    }
}
