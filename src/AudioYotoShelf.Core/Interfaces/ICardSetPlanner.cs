using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Packs a series' books, in order, onto Yoto cards (docs/specs/series-card-sets.md §5). Pure: no
/// I/O, and "fits" is decided only by <see cref="ICardCapacityCalculator"/>, so the plan follows
/// whatever <c>YotoCardLimits</c> says.
/// </summary>
public interface ICardSetPlanner
{
    /// <param name="books">Each book's per-chapter tracks (one per file, chapter, or the whole file).</param>
    /// <param name="grouping">The grouping the cards' playlists use, applied before checking capacity.</param>
    /// <param name="standalone">Book indices that must sit on cards of their own even if they would share.</param>
    CardSetPlan Plan(
        IReadOnlyList<BookTrackPlan> books, PackingMode mode,
        TrackGrouping grouping = TrackGrouping.Auto, IReadOnlySet<int>? standalone = null);

    /// <summary>
    /// Builds the plan from user-chosen breaks (index of the first book of each card after the first)
    /// and split points (per split book, index of the first chapter of each part after the first).
    /// A split book without split points is split by <paramref name="mode"/>. Breaks around books
    /// that need splitting are added when missing. Throws <see cref="ArgumentException"/> for breaks
    /// or split points that are out of order or out of range.
    /// </summary>
    CardSetPlan Apply(
        IReadOnlyList<BookTrackPlan> books, IReadOnlyList<int> breaks,
        IReadOnlyDictionary<string, IReadOnlyList<int>> splitPoints,
        PackingMode mode = PackingMode.Balance, TrackGrouping grouping = TrackGrouping.Auto);
}
