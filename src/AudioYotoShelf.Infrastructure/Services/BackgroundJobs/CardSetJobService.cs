using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services.BackgroundJobs;

public interface ICardSetJobService
{
    /// <summary>
    /// Sends the given cards of a set one after another. No automatic retry: a retry would re-send
    /// cards that already reached Yoto. A failed card is retried by sending it again on its own.
    /// </summary>
    [Queue("transfers")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    Task ExecuteCardSetTransferAsync(Guid cardSetId, Guid[] playlistIds, CancellationToken ct);
}

public class CardSetJobService(
    IPlaylistTransferOrchestrator orchestrator,
    AudioYotoShelfDbContext db,
    ICardSetProgressNotifier notifier,
    ILogger<CardSetJobService> logger) : ICardSetJobService
{
    public async Task ExecuteCardSetTransferAsync(Guid cardSetId, Guid[] playlistIds, CancellationToken ct)
    {
        logger.LogInformation("Hangfire: sending {Count} cards of card set {CardSetId}", playlistIds.Length, cardSetId);

        await SendsChangedAsync(cardSetId);
        try
        {
            var failed = 0;
            for (var i = 0; i < playlistIds.Length; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    await SetStatusAsync(playlistIds[i..], PlaylistStatus.Queued, PlaylistStatus.Draft, null);
                    await NotifyAsync(cardSetId, playlistIds[i..]);
                    ct.ThrowIfCancellationRequested();
                }

                // Send only a card this job still owns: Queued, or Transferring when a restart cut the last
                // run short. Hangfire re-runs an interrupted job, and by then a card may be on Yoto, back
                // to draft, re-packed, or queued again by a newer send that will send it itself.
                if (await StatusOfAsync(playlistIds[i]) is not (PlaylistStatus.Queued or PlaylistStatus.Transferring))
                    continue;

                try
                {
                    await NotifyAsync(cardSetId, [playlistIds[i]], PlaylistStatus.Transferring);
                    await orchestrator.TransferPlaylistAsync(playlistIds[i], ct);
                    await NotifyAsync(cardSetId, [playlistIds[i]]);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    await SetStatusAsync(playlistIds[i..], PlaylistStatus.Queued, PlaylistStatus.Draft, null);
                    await NotifyAsync(cardSetId, playlistIds[i..]);
                    throw;
                }
                catch (Exception ex)
                {
                    // The orchestrator marks a card Failed once it starts sending it. A card that failed
                    // before that (no connection, missing playlist) would otherwise stay Queued for good.
                    await SetStatusAsync([playlistIds[i]], PlaylistStatus.Queued, PlaylistStatus.Failed, ex.Message);
                    await NotifyAsync(cardSetId, [playlistIds[i]]);
                    failed++;
                    logger.LogError(ex, "Card set {CardSetId}: card {Index}/{Total} ({PlaylistId}) failed",
                        cardSetId, i + 1, playlistIds.Length, playlistIds[i]);
                }
            }

            logger.LogInformation("Card set {CardSetId}: {Sent} of {Total} cards sent",
                cardSetId, playlistIds.Length - failed, playlistIds.Length);
        }
        finally
        {
            await SendsChangedAsync(cardSetId);
        }
    }

    /// <summary>
    /// Tells the series page each card's state as stored, or <paramref name="announced"/> for a card
    /// about to start. Best effort: a page that misses one still catches up on its next read.
    /// </summary>
    private async Task NotifyAsync(Guid cardSetId, Guid[] playlistIds, PlaylistStatus? announced = null)
    {
        try
        {
            var cards = await db.Playlists.AsNoTracking()
                .Where(p => playlistIds.Contains(p.Id))
                .Select(p => new { p.Id, p.SetIndex, p.Status, p.ErrorMessage })
                .ToListAsync(CancellationToken.None);
            foreach (var card in cards)
                await notifier.CardChangedAsync(new CardSetProgressUpdate(
                    cardSetId, card.Id, card.SetIndex, announced ?? card.Status, announced is null ? card.ErrorMessage : null));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Card set {CardSetId}: could not push card progress", cardSetId);
        }
    }

    /// <summary>Tells the Transfers page the set started or stopped sending. Best effort, like <see cref="NotifyAsync"/>.</summary>
    private async Task SendsChangedAsync(Guid cardSetId)
    {
        try
        {
            await notifier.SendsChangedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Card set {CardSetId}: could not push that the sends changed", cardSetId);
        }
    }

    private async Task<PlaylistStatus?> StatusOfAsync(Guid playlistId) =>
        await db.Playlists.Where(p => p.Id == playlistId).Select(p => (PlaylistStatus?)p.Status)
            .FirstOrDefaultAsync(CancellationToken.None);

    private async Task SetStatusAsync(Guid[] playlistIds, PlaylistStatus from, PlaylistStatus to, string? error)
    {
        var cards = await db.Playlists
            .Where(p => playlistIds.Contains(p.Id) && p.Status == from)
            .ToListAsync(CancellationToken.None);
        foreach (var card in cards)
        {
            card.Status = to;
            card.ErrorMessage = error is null ? null : error.Length > 4000 ? error[..4000] : error;
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
