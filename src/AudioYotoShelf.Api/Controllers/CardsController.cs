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
    /// List user's Yoto MYO cards with transfer history enrichment.
    /// </summary>
    [HttpGet("{userConnectionId:guid}")]
    public async Task<IActionResult> GetCards(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidYotoConnection)
            return Unauthorized("No valid Yoto connection");

        var cards = await yotoService.GetUserCardsAsync(user.YotoAccessToken!, ct);

        // Enrich with transfer history: which cards were created by this app
        var transferredCardIds = await db.CardTransfers
            .Where(t => t.UserConnectionId == userConnectionId && t.YotoCardId != null)
            .Select(t => new { t.YotoCardId, t.BookTitle, t.BookAuthor })
            .ToListAsync(ct);

        var cardIdLookup = transferredCardIds.ToDictionary(t => t.YotoCardId!, t => t);

        var enriched = cards.Select(card => new
        {
            card.CardId,
            card.Metadata,
            ChapterCount = card.Content?.Chapters?.Length ?? 0,
            TrackCount = card.Content?.Chapters?.Sum(ch => ch.Tracks?.Length ?? 0) ?? 0,
            FromAudioYotoShelf = cardIdLookup.ContainsKey(card.CardId),
            SourceBookTitle = cardIdLookup.TryGetValue(card.CardId, out var tx) ? tx.BookTitle : null,
            SourceBookAuthor = cardIdLookup.TryGetValue(card.CardId, out var tx2) ? tx2.BookAuthor : null,
        }).ToArray();

        return Ok(enriched);
    }

    /// <summary>
    /// Get full card content including chapters and tracks.
    /// </summary>
    [HttpGet("{userConnectionId:guid}/{cardId}")]
    public async Task<IActionResult> GetCard(Guid userConnectionId, string cardId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidYotoConnection)
            return Unauthorized("No valid Yoto connection");

        var card = await yotoService.GetCardContentAsync(user.YotoAccessToken!, cardId, ct);
        return Ok(card);
    }

    /// <summary>
    /// Delete a Yoto MYO card.
    /// </summary>
    [HttpDelete("{userConnectionId:guid}/{cardId}")]
    public async Task<IActionResult> DeleteCard(Guid userConnectionId, string cardId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null || !user.HasValidYotoConnection)
            return Unauthorized("No valid Yoto connection");

        await yotoService.DeleteCardAsync(user.YotoAccessToken!, cardId, ct);

        logger.LogInformation("Deleted Yoto card {CardId} for user {Username}", cardId, user.Username);

        return Ok(new { Message = "Card deleted", CardId = cardId });
    }
}
