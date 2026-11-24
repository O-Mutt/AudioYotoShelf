using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>
/// Checks one book against the card limits as the single-book transfer will upload it. Shared by
/// the book endpoint (refuse before queueing) and <see cref="TransferOrchestrator"/> (guard in the
/// job) so both reach the same verdict.
/// </summary>
public static class BookCapacity
{
    private const string SendAnyway =
        "\"Send anyway\" on the book page tries it as one card, which Yoto may refuse or not play to the end.";

    public static CapacityRefusal? Check(AbsLibraryItem item, ITrackPlanner planner, ICardCapacityCalculator calculator)
    {
        if (item.Media is not { } media) return null;

        // Chapters grouping is the track list TransferOrchestrator builds: one track per file, per
        // chapter of a single file, or the whole file. It uploads them without splitting, so the
        // projection must not count split pieces either.
        var title = media.Metadata.Title ?? "Unknown";
        var plan = new BookTrackPlan(item.Id, title, planner.Plan(media, TrackGrouping.Chapters));
        var result = calculator.Calculate([plan], splitTracks: false);
        if (result.WithinLimits) return null;

        return new CapacityRefusal(
            RefusalMessage(title, media.Metadata.Series?.FirstOrDefault()?.Name, result),
            PlaylistCapacity.ToSummary(result));
    }

    private static string RefusalMessage(string title, string? seriesName, CapacityResult result)
    {
        var why = string.Join(" ", result.Messages);
        var next = seriesName is not null
            ? $"It is in the series \"{seriesName}\": the card plan on that series page can split it across cards. {SendAnyway}"
            : SendAnyway;
        return $"\"{title}\" is larger than one Yoto card, so it was not sent. {why} {next}";
    }
}
