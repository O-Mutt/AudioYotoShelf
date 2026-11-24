using AudioYotoShelf.Core.DTOs.Playlist;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Projects a set of book track plans against the Yoto card limits, accounting for
/// the per-track splitting that the transfer pipeline applies to oversized tracks.
/// </summary>
public interface ICardCapacityCalculator
{
    /// <summary>Number of compliant sub-tracks an oversized source track will be split into (>= 1).</summary>
    int ProjectedTrackCount(SourceTrack track);

    /// <param name="books">The books on the card, in order.</param>
    /// <param name="splitTracks">
    /// False counts each source track as one card track, for a transfer that uploads tracks as they
    /// are (the single-book transfer) rather than splitting them to the per-track limits.
    /// </param>
    CapacityResult Calculate(IEnumerable<BookTrackPlan> books, bool splitTracks = true);
}
