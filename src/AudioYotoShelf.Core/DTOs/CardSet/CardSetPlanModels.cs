using AudioYotoShelf.Core.DTOs.Playlist;

namespace AudioYotoShelf.Core.DTOs.CardSet;

/// <summary>
/// One card of a plan. Whole books leave the chapter range and part numbers null; one part of a
/// split book has <c>FirstBookIndex == LastBookIndex</c> and the chapter range set (inclusive
/// indices into that book's entry in <see cref="CardSetPlan.SplitChapters"/>).
/// </summary>
public record PlannedCard(
    int FirstBookIndex,
    int LastBookIndex,
    int? FirstChapterIndex,
    int? LastChapterIndex,
    int? PartNumber,
    int? PartCount,
    CapacityResult Capacity)
{
    public bool IsPart => FirstChapterIndex is not null;
}

/// <summary>
/// The planner's answer for a series. <see cref="SplitChapters"/> holds, per split book, the chapter
/// list the chapter ranges index into: the book's own chapters, or equal time segments for a book
/// without chapters (spec §5.4 step 3).
/// </summary>
public record CardSetPlan(
    IReadOnlyList<PlannedCard> Cards,
    bool WithinLimits,
    IReadOnlyList<string> UnsplittableBookIds,
    IReadOnlyList<string> Messages,
    IReadOnlyDictionary<string, IReadOnlyList<SourceTrack>> SplitChapters);
