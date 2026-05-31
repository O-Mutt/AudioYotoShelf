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

    CapacityResult Calculate(IEnumerable<BookTrackPlan> books);
}
