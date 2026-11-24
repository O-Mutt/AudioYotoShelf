using AudioYotoShelf.Core.Interfaces;
using Hangfire;
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
    ILogger<CardSetJobService> logger) : ICardSetJobService
{
    public async Task ExecuteCardSetTransferAsync(Guid cardSetId, Guid[] playlistIds, CancellationToken ct)
    {
        logger.LogInformation("Hangfire: sending {Count} cards of card set {CardSetId}", playlistIds.Length, cardSetId);

        var failed = 0;
        for (var i = 0; i < playlistIds.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await orchestrator.TransferPlaylistAsync(playlistIds[i], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The orchestrator has already marked this card Failed with the reason; the
                // remaining cards don't depend on it, so keep going.
                failed++;
                logger.LogError(ex, "Card set {CardSetId}: card {Index}/{Total} ({PlaylistId}) failed",
                    cardSetId, i + 1, playlistIds.Length, playlistIds[i]);
            }
        }

        logger.LogInformation("Card set {CardSetId}: {Sent} of {Total} cards sent",
            cardSetId, playlistIds.Length - failed, playlistIds.Length);
    }
}
