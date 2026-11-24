using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

/// <summary>Series card sets (docs/specs/series-card-sets.md §7).</summary>
[ApiController]
[Route("api/[controller]")]
public class CardSetsController(
    ICardSetService cardSets,
    IBackgroundJobClient backgroundJobs,
    AudioYotoShelfDbContext db,
    ILogger<CardSetsController> logger) : AppControllerBase
{
    /// <summary>Previews how a series packs onto cards. Writes nothing.</summary>
    [HttpPost("plan")]
    public async Task<IActionResult> Plan([FromBody] CardSetPlanRequest request, CancellationToken ct)
        => Ok(await cardSets.PlanAsync(CurrentUserConnectionId, request, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCardSetRequest request, CancellationToken ct)
    {
        try
        {
            var result = await cardSets.CreateAsync(CurrentUserConnectionId, request, ct);
            return CreatedAtAction(nameof(Get), new { cardSetId = result.Id }, result);
        }
        catch (CardSetConflictException ex)
        {
            return Conflict(new { ex.Message, ex.CardSetId });
        }
        catch (CardSetPlanException ex)
        {
            return UnprocessableEntity(new { ex.Message, ex.Plan });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? seriesId, CancellationToken ct)
        => Ok(await cardSets.ListAsync(CurrentUserConnectionId, seriesId, ct));

    /// <param name="newBooks">false skips reading Audiobookshelf for new books, for frequent re-reads while sending.</param>
    [HttpGet("{cardSetId:guid}")]
    public async Task<IActionResult> Get(Guid cardSetId, CancellationToken ct, [FromQuery] bool newBooks = true)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();
        var result = await cardSets.GetAsync(cardSetId, newBooks, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Card-set badges for a page of the series grid: <c>?seriesIds=a,b,c</c>.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? seriesIds, CancellationToken ct)
    {
        var ids = (seriesIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Ok(ids.Length == 0 ? [] : await cardSets.StatusAsync(CurrentUserConnectionId, ids, ct));
    }

    [HttpGet("limits")]
    public IActionResult Limits() => Ok(cardSets.Limits());

    /// <summary>Places books added to the series since the set was made.</summary>
    [HttpPost("{cardSetId:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid cardSetId, CancellationToken ct)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();
        try
        {
            return Ok(await cardSets.RefreshAsync(cardSetId, ct));
        }
        catch (CardSetPlanException ex)
        {
            return UnprocessableEntity(new { ex.Message, ex.Plan });
        }
    }

    [HttpPut("{cardSetId:guid}/breaks")]
    public async Task<IActionResult> UpdateBreaks(
        Guid cardSetId, [FromBody] UpdateCardSetBreaksRequest request, CancellationToken ct)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();
        try
        {
            return Ok(await cardSets.UpdateBreaksAsync(cardSetId, request, ct));
        }
        catch (CardSetConflictException ex)
        {
            return Conflict(new { ex.Message, ex.CardSetId });
        }
        catch (CardSetPlanException ex)
        {
            return UnprocessableEntity(new { ex.Message, ex.Plan });
        }
    }

    [HttpPatch("{cardSetId:guid}")]
    public async Task<IActionResult> Update(
        Guid cardSetId, [FromBody] UpdateCardSetRequest request, CancellationToken ct)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();
        try
        {
            return Ok(await cardSets.UpdateAsync(cardSetId, request, ct));
        }
        catch (CardSetPlanException ex)
        {
            return UnprocessableEntity(new { ex.Message, ex.Plan });
        }
    }

    [HttpPatch("{cardSetId:guid}/cards/{playlistId:guid}")]
    public async Task<IActionResult> RenameCard(
        Guid cardSetId, Guid playlistId, [FromBody] RenameCardSetCardRequest request, CancellationToken ct)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();
        return Ok(await cardSets.RenameCardAsync(cardSetId, playlistId, request.Name, ct));
    }

    /// <summary>Deletes the set. Its cards stay as standalone playlists; nothing on Yoto changes.</summary>
    [HttpDelete("{cardSetId:guid}")]
    public async Task<IActionResult> Delete(Guid cardSetId, CancellationToken ct)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();
        await cardSets.DeleteAsync(cardSetId, ct);
        return Ok(new { Message = "Card set deleted" });
    }

    /// <summary>
    /// Queues the cards to send, one after another. Blocks (409) when a requested card is over the
    /// card limits or already being sent.
    /// </summary>
    [HttpPost("{cardSetId:guid}/transfer")]
    public async Task<IActionResult> Transfer(
        Guid cardSetId, [FromBody] TransferCardSetRequest? request, CancellationToken ct)
    {
        if (!await OwnsCardSetAsync(cardSetId, ct)) return NotFound();

        Guid[] playlistIds;
        try
        {
            playlistIds = await cardSets.PrepareTransferAsync(cardSetId, request?.PlaylistIds, ct);
        }
        catch (CardSetCapacityException ex)
        {
            return Conflict(new { ex.Message, ex.PlaylistId, ex.Capacity });
        }
        catch (CardSetConflictException ex)
        {
            return Conflict(new { ex.Message, ex.CardSetId });
        }

        if (playlistIds.Length == 0)
            return Ok(new { CardSetId = cardSetId, Message = "Every card is already on Yoto" });

        string jobId;
        try
        {
            jobId = backgroundJobs.Enqueue<ICardSetJobService>(
                svc => svc.ExecuteCardSetTransferAsync(cardSetId, playlistIds, CancellationToken.None));
        }
        catch
        {
            // The cards were marked Queued; without a job nothing would ever move them on.
            await cardSets.ReleaseQueuedAsync(playlistIds, CancellationToken.None);
            throw;
        }

        logger.LogInformation("Card set transfer queued: {CardSetId} ({Count} cards) -> Job {JobId}",
            cardSetId, playlistIds.Length, jobId);
        return Accepted(new { CardSetId = cardSetId, JobId = jobId, PlaylistIds = playlistIds });
    }

    private async Task<bool> OwnsCardSetAsync(Guid cardSetId, CancellationToken ct)
    {
        var owner = await db.CardSets
            .Where(s => s.Id == cardSetId)
            .Select(s => (Guid?)s.UserConnectionId)
            .FirstOrDefaultAsync(ct);
        return owner is not null && owner == CurrentUserConnectionId;
    }
}
