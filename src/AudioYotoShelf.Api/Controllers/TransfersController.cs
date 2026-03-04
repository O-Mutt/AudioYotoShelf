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
    public async Task<IActionResult> TransferBook(
        Guid userConnectionId,
        [FromBody] CreateTransferRequest request,
        CancellationToken ct)
    {
        // Guard against duplicate transfers for the same item
        var hasActive = await db.CardTransfers.AnyAsync(
            t => t.UserConnectionId == userConnectionId
                 && t.AbsLibraryItemId == request.AbsLibraryItemId
                 && (t.Status == Core.Enums.TransferStatus.Pending
                     || t.Status == Core.Enums.TransferStatus.DownloadingAudio
                     || t.Status == Core.Enums.TransferStatus.UploadingToYoto
                     || t.Status == Core.Enums.TransferStatus.AwaitingTranscode
                     || t.Status == Core.Enums.TransferStatus.GeneratingIcons
                     || t.Status == Core.Enums.TransferStatus.CreatingCard), ct);

        if (hasActive)
            return Conflict(new { Message = "A transfer is already in progress for this book" });

        var transferId = Guid.NewGuid();
        var jobId = backgroundJobs.Enqueue<ITransferJobService>(
            svc => svc.ExecuteBookTransferAsync(userConnectionId, request, transferId, CancellationToken.None));

        logger.LogInformation("Book transfer queued: {ItemId} → Transfer {TransferId}, Job {JobId}",
            request.AbsLibraryItemId, transferId, jobId);

        return Accepted(new { TransferId = transferId, JobId = jobId, Message = "Transfer queued" });
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
            var transferId = Guid.NewGuid();
            var jobId = backgroundJobs.Enqueue<ITransferJobService>(
                svc => svc.ExecuteBookTransferAsync(userConnectionId, bookRequest, transferId, CancellationToken.None));
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

    [HttpDelete("{transferId:guid}")]
    public async Task<IActionResult> DeleteTransfer(Guid transferId, CancellationToken ct)
    {
        var transfer = await db.CardTransfers
            .Include(t => t.TrackMappings)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct);

        if (transfer is null) return NotFound();

        if (transfer.Status is not (Core.Enums.TransferStatus.Completed
            or Core.Enums.TransferStatus.Failed
            or Core.Enums.TransferStatus.Cancelled))
            return Conflict(new { Message = "Can only delete completed, failed, or cancelled transfers" });

        db.TrackMappings.RemoveRange(transfer.TrackMappings);
        db.CardTransfers.Remove(transfer);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Deleted transfer {TransferId} ({BookTitle})", transferId, transfer.BookTitle);
        return Ok(new { Message = "Transfer deleted" });
    }

    private static TransferResponse MapToResponse(Core.Entities.CardTransfer t) => new(
        t.Id,
        t.AbsLibraryItemId,
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
