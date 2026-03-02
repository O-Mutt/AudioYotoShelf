using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransfersController(
    AudioYotoShelfDbContext db,
    ITransferOrchestrator orchestrator,
    IBackgroundJobClient backgroundJobs,
    ILogger<TransfersController> logger) : ControllerBase
{
    [HttpGet("{userConnectionId:guid}")]
    public async Task<IActionResult> GetTransfers(
        Guid userConnectionId,
        [FromQuery] int page = 0, [FromQuery] int limit = 20,
        [FromQuery] TransferStatus? status = null,
        CancellationToken ct = default)
    {
        var query = db.CardTransfers
            .Where(t => t.UserConnectionId == userConnectionId)
            .Include(t => t.TrackMappings)
                .ThenInclude(tm => tm.GeneratedIcon)
            .OrderByDescending(t => t.CreatedAt)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var total = await query.CountAsync(ct);
        var transfers = await query.Skip(page * limit).Take(limit).ToArrayAsync(ct);

        return Ok(new
        {
            Results = transfers.Select(MapToResponse).ToArray(),
            Total = total,
            Page = page,
            Limit = limit
        });
    }

    [HttpGet("detail/{transferId:guid}")]
    public async Task<IActionResult> GetTransfer(Guid transferId, CancellationToken ct)
    {
        var transfer = await db.CardTransfers
            .Include(t => t.TrackMappings)
                .ThenInclude(tm => tm.GeneratedIcon)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct);

        if (transfer is null) return NotFound();
        return Ok(MapToResponse(transfer));
    }

    [HttpPost("{userConnectionId:guid}/book")]
    public IActionResult TransferBook(
        Guid userConnectionId,
        [FromBody] CreateTransferRequest request)
    {
        var jobId = backgroundJobs.Enqueue<ITransferJobService>(
            svc => svc.ExecuteBookTransferAsync(userConnectionId, request, CancellationToken.None));

        logger.LogInformation("Book transfer queued: {ItemId} → Job {JobId}",
            request.AbsLibraryItemId, jobId);

        return Accepted(new { JobId = jobId, Message = "Transfer queued" });
    }

    [HttpPost("{userConnectionId:guid}/series")]
    public IActionResult TransferSeries(
        Guid userConnectionId,
        [FromBody] CreateSeriesTransferRequest request)
    {
        var jobId = backgroundJobs.Enqueue<ITransferJobService>(
            svc => svc.ExecuteSeriesTransferAsync(userConnectionId, request, CancellationToken.None));

        logger.LogInformation("Series transfer queued: {SeriesId} → Job {JobId}",
            request.AbsSeriesId, jobId);

        return Accepted(new { JobId = jobId, Message = "Series transfer queued" });
    }

    /// <summary>
    /// Phase 2: Batch transfer — enqueues one Hangfire job per book, returns batch summary.
    /// ISP: BatchTransferRequest is its own DTO, not overloading CreateTransferRequest.
    /// </summary>
    [HttpPost("{userConnectionId:guid}/batch")]
    public IActionResult TransferBatch(
        Guid userConnectionId,
        [FromBody] BatchTransferRequest request)
    {
        var jobIds = new List<string>();
        foreach (var itemId in request.AbsLibraryItemIds)
        {
            var bookRequest = new CreateTransferRequest(
                AbsLibraryItemId: itemId,
                Category: request.Category,
                PlaybackType: request.PlaybackType,
                OverrideMinAge: request.OverrideMinAge,
                OverrideMaxAge: request.OverrideMaxAge
            );
            var jobId = backgroundJobs.Enqueue<ITransferJobService>(
                svc => svc.ExecuteBookTransferAsync(userConnectionId, bookRequest, CancellationToken.None));
            jobIds.Add(jobId);
        }

        logger.LogInformation("Batch transfer queued: {Count} books → {JobCount} jobs",
            request.AbsLibraryItemIds.Length, jobIds.Count);

        var batchId = Guid.NewGuid().ToString("N")[..12];
        return Accepted(new BatchTransferResponse(batchId, request.AbsLibraryItemIds.Length, jobIds.Count, jobIds.ToArray()));
    }

    [HttpPost("retry/{transferId:guid}")]
    public IActionResult RetryTransfer(Guid transferId)
    {
        var jobId = backgroundJobs.Enqueue<ITransferJobService>(
            svc => svc.ExecuteRetryTransferAsync(transferId, CancellationToken.None));

        return Accepted(new { JobId = jobId, Message = "Retry queued" });
    }

    [HttpPost("cancel/{transferId:guid}")]
    public async Task<IActionResult> CancelTransfer(Guid transferId, CancellationToken ct)
    {
        await orchestrator.CancelTransferAsync(transferId, ct);
        return Ok(new { Message = "Transfer cancelled" });
    }

    private static TransferResponse MapToResponse(Core.Entities.CardTransfer t) => new(
        t.Id,
        t.BookTitle,
        t.BookAuthor,
        t.SeriesName,
        t.SeriesSequence,
        t.Status,
        t.ProgressPercent,
        t.ErrorMessage,
        new AgeRangeResponse(
            t.SuggestedMinAge, t.SuggestedMaxAge,
            t.AgeSuggestionReason, t.AgeSuggestionSource,
            t.OverrideMinAge, t.OverrideMaxAge,
            t.EffectiveMinAge, t.EffectiveMaxAge),
        t.YotoCardId,
        t.CreatedAt,
        t.CompletedAt,
        t.TrackMappings.Select(tm => new TrackMappingResponse(
            tm.Id, tm.ChapterTitle, tm.ChapterIndex,
            tm.EndTime - tm.StartTime, tm.IsUploaded,
            tm.GeneratedIcon?.YotoIconUrl)).ToArray()
    );
}
