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
    ILogger<CardSetJobService> logger) : ICardSetJobService
{
    public async Task ExecuteCardSetTransferAsync(Guid cardSetId, Guid[] playlistIds, CancellationToken ct)
    {
        logger.LogInformation("Hangfire: sending {Count} cards of card set {CardSetId}", playlistIds.Length, cardSetId);

        var failed = 0;
        for (var i = 0; i < playlistIds.Length; i++)
        {
            if (ct.IsCancellationRequested)
            {
                await SetStatusAsync(playlistIds[i..], PlaylistStatus.Queued, PlaylistStatus.Draft, null);
                ct.ThrowIfCancellationRequested();
            }

            // Hangfire re-runs a job that a server restart interrupted; cards it already sent stay as they are.
            if (await StatusOfAsync(playlistIds[i]) == PlaylistStatus.Transferred)
                continue;

            try
            {
                await orchestrator.TransferPlaylistAsync(playlistIds[i], ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await SetStatusAsync(playlistIds[i..], PlaylistStatus.Queued, PlaylistStatus.Draft, null);
                throw;
            }
            catch (Exception ex)
            {
                // The orchestrator marks a card Failed once it starts sending it. A card that failed
                // before that (no connection, missing playlist) would otherwise stay Queued for good.
                await SetStatusAsync([playlistIds[i]], PlaylistStatus.Queued, PlaylistStatus.Failed, ex.Message);
                failed++;
                logger.LogError(ex, "Card set {CardSetId}: card {Index}/{Total} ({PlaylistId}) failed",
                    cardSetId, i + 1, playlistIds.Length, playlistIds[i]);
            }
        }

        logger.LogInformation("Card set {CardSetId}: {Sent} of {Total} cards sent",
            cardSetId, playlistIds.Length - failed, playlistIds.Length);
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
