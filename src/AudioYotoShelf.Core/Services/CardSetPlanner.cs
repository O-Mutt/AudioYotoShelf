using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;

namespace AudioYotoShelf.Core.Services;

public class CardSetPlanner(
    ICardCapacityCalculator calculator, ITrackPlanner trackPlanner, YotoCardLimits limits) : ICardSetPlanner
{
    private readonly ICardCapacityCalculator _calculator = calculator;
    private readonly ITrackPlanner _trackPlanner = trackPlanner;
    private readonly YotoCardLimits _limits = limits;

    public CardSetPlan Plan(
        IReadOnlyList<BookTrackPlan> books, PackingMode mode,
        TrackGrouping grouping = TrackGrouping.Auto, IReadOnlySet<int>? standalone = null)
    {
        var series = new Series(this, books, grouping);
        var result = new PlanBuilder();

        var runStart = 0;
        for (var i = 0; i <= books.Count; i++)
        {
            var alone = i < books.Count && (series.Oversized(i) || standalone?.Contains(i) == true);
            if (i < books.Count && !alone) continue;

            if (i > runStart)
                PackBooks(series, runStart, i - 1, mode, result);
            if (i < books.Count)
                PlaceAlone(series, i, splitPoints: null, mode, result);
            runStart = i + 1;
        }

        return result.Build();
    }

    public CardSetPlan Apply(
        IReadOnlyList<BookTrackPlan> books, IReadOnlyList<int> breaks,
        IReadOnlyDictionary<string, IReadOnlyList<int>> splitPoints,
        PackingMode mode = PackingMode.Balance, TrackGrouping grouping = TrackGrouping.Auto)
    {
        RequireIncreasingWithin(breaks, 1, books.Count - 1, "Breaks");

        var series = new Series(this, books, grouping);
        var allBreaks = new SortedSet<int>(breaks);
        for (var i = 0; i < books.Count; i++)
        {
            if (!series.Oversized(i)) continue;
            if (i > 0) allBreaks.Add(i);
            if (i + 1 < books.Count) allBreaks.Add(i + 1);
        }

        var result = new PlanBuilder();
        var starts = new List<int> { 0 };
        starts.AddRange(allBreaks);
        for (var c = 0; c < starts.Count && books.Count > 0; c++)
        {
            var first = starts[c];
            var last = c + 1 < starts.Count ? starts[c + 1] - 1 : books.Count - 1;

            if (first == last && series.Oversized(first))
            {
                splitPoints.TryGetValue(books[first].AbsLibraryItemId, out var points);
                PlaceAlone(series, first, points, mode, result);
            }
            else
            {
                result.AddCard(new PlannedCard(first, last, null, null, null, null, series.BooksCapacity(first, last)));
            }
        }

        return result.Build();
    }

    // =========================================================================
    // Placing books
    // =========================================================================

    private void PackBooks(Series series, int first, int last, PackingMode mode, PlanBuilder result)
    {
        var runs = Pack(last - first + 1, (a, b) => series.BooksCapacity(first + a, first + b), mode)
            ?? throw new InvalidOperationException("A book that fits a card alone failed to pack.");

        foreach (var (a, b) in runs)
            result.AddCard(new PlannedCard(first + a, first + b, null, null, null, null,
                series.BooksCapacity(first + a, first + b)));
    }

    /// <summary>Places a book on cards of its own: one whole card if it fits, otherwise split by chapter.</summary>
    private void PlaceAlone(
        Series series, int bookIndex, IReadOnlyList<int>? splitPoints, PackingMode mode, PlanBuilder result)
    {
        if (!series.Oversized(bookIndex))
        {
            result.AddCard(new PlannedCard(bookIndex, bookIndex, null, null, null, null,
                series.BooksCapacity(bookIndex, bookIndex)));
            return;
        }

        var book = series.Books[bookIndex];
        var chapters = SplittableChapters(book);
        var partCapacity = (int a, int b) => series.ChaptersCapacity(bookIndex, chapters, a, b);

        IReadOnlyList<(int First, int Last)>? parts;
        if (splitPoints is null)
        {
            parts = Pack(chapters.Count, partCapacity, mode);
        }
        else
        {
            RequireIncreasingWithin(splitPoints, 1, chapters.Count - 1, $"Split points for {book.BookTitle}");
            var starts = new[] { 0 }.Concat(splitPoints).ToList();
            parts = starts
                .Select((start, i) => (start, i + 1 < starts.Count ? starts[i + 1] - 1 : chapters.Count - 1))
                .ToList();
        }

        if (parts is null)
        {
            result.Unsplittable(book, $"{book.BookTitle} has a chapter longer than one card holds, so it cannot be split; it was left out.");
            return;
        }

        result.SplitChapters[book.AbsLibraryItemId] = chapters;
        for (var p = 0; p < parts.Count; p++)
        {
            var (a, b) = parts[p];
            result.AddCard(new PlannedCard(bookIndex, bookIndex, a, b, p + 1, parts.Count, partCapacity(a, b)));
        }
    }

    /// <summary>
    /// The units a book splits on. A book with one entry (a single file without chapters) is cut into
    /// equal segments no longer than one track, the same cut the transfer makes for long tracks.
    /// </summary>
    internal IReadOnlyList<SourceTrack> SplittableChapters(BookTrackPlan book)
    {
        if (book.Tracks.Count != 1) return book.Tracks;

        var whole = book.Tracks[0];
        var segments = (int)Math.Ceiling(whole.DurationSeconds / _limits.MaxTrackDurationSeconds);
        if (segments <= 1) return book.Tracks;

        return Enumerable.Range(0, segments)
            .Select(i => new SourceTrack($"{whole.Title} ({i + 1})",
                whole.DurationSeconds / segments, whole.EstimatedBytes / segments))
            .ToList();
    }

    // =========================================================================
    // Packing (spec §5.2, §5.3)
    // =========================================================================

    /// <summary>
    /// Packs items 0..count-1 into contiguous runs. Returns null when one item alone does not fit.
    /// Fill closes a card as soon as the next item would overflow it; Balance keeps Fill's card count
    /// and moves the breaks to make the fullest card as empty as possible.
    /// </summary>
    private IReadOnlyList<(int First, int Last)>? Pack(
        int count, Func<int, int, CapacityResult> capacity, PackingMode mode)
    {
        var fill = PackFill(count, capacity);
        if (fill is null || mode == PackingMode.Fill || fill.Count <= 1) return fill;
        return PackBalanced(count, fill.Count, capacity);
    }

    private static List<(int First, int Last)>? PackFill(int count, Func<int, int, CapacityResult> capacity)
    {
        var runs = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i < count; i++)
        {
            if (capacity(start, i).WithinLimits) continue;
            if (start == i || !capacity(i, i).WithinLimits) return null;
            runs.Add((start, i - 1));
            start = i;
        }
        if (count > 0) runs.Add((start, count - 1));
        return runs;
    }

    private List<(int First, int Last)> PackBalanced(
        int count, int cards, Func<int, int, CapacityResult> capacity)
    {
        var fullness = new Dictionary<(int, int), double>();
        double Full(int a, int b)
        {
            if (!fullness.TryGetValue((a, b), out var f))
                fullness[(a, b)] = f = Fullness(capacity(a, b));
            return f;
        }

        // best[i, c]: the smallest possible fullest card when items 0..i-1 fill c cards.
        var best = new double[count + 1, cards + 1];
        var from = new int[count + 1, cards + 1];
        for (var i = 0; i <= count; i++)
            for (var c = 0; c <= cards; c++)
                best[i, c] = double.PositiveInfinity;
        best[0, 0] = 0;

        for (var c = 1; c <= cards; c++)
            for (var i = c; i <= count; i++)
                for (var j = c - 1; j < i; j++)
                {
                    if (double.IsPositiveInfinity(best[j, c - 1])) continue;
                    var candidate = Math.Max(best[j, c - 1], Full(j, i - 1));
                    // "<=" keeps the latest break on a tie, so earlier cards come out fuller and the
                    // last card keeps the most room for books added to the series later.
                    if (candidate <= best[i, c] && !double.IsPositiveInfinity(candidate))
                    {
                        best[i, c] = candidate;
                        from[i, c] = j;
                    }
                }

        var runs = new List<(int, int)>();
        for (int i = count, c = cards; c > 0; c--)
        {
            var j = from[i, c];
            runs.Add((j, i - 1));
            i = j;
        }
        runs.Reverse();
        return runs;
    }

    private double Fullness(CapacityResult r)
    {
        if (!r.WithinLimits) return double.PositiveInfinity;
        var full = Math.Max(
            (double)r.Projected.Tracks / _limits.MaxTracks,
            (double)r.Projected.Bytes / _limits.MaxTotalBytes);
        if (_limits.MaxTotalDurationSeconds is { } maxDuration)
            full = Math.Max(full, r.Projected.DurationSeconds / maxDuration);
        return full;
    }

    private static void RequireIncreasingWithin(IReadOnlyList<int> values, int min, int max, string what)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] < min || values[i] > max)
                throw new ArgumentException($"{what} must be between {min} and {max}; got {values[i]}.");
            if (i > 0 && values[i] <= values[i - 1])
                throw new ArgumentException($"{what} must be in increasing order.");
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>The series with each book's grouped tracks and capacity cached for repeated lookups.</summary>
    private sealed class Series(CardSetPlanner owner, IReadOnlyList<BookTrackPlan> books, TrackGrouping grouping)
    {
        private readonly BookTrackPlan[] _grouped = books
            .Select(b => b with { Tracks = owner._trackPlanner.ApplyGrouping(b.Tracks, grouping, b.BookTitle) })
            .ToArray();
        private readonly Dictionary<int, bool> _oversized = [];

        public IReadOnlyList<BookTrackPlan> Books => books;

        public bool Oversized(int i)
        {
            if (!_oversized.TryGetValue(i, out var oversized))
                _oversized[i] = oversized = !BooksCapacity(i, i).WithinLimits;
            return oversized;
        }

        public CapacityResult BooksCapacity(int first, int last) =>
            owner._calculator.Calculate(_grouped[first..(last + 1)]);

        public CapacityResult ChaptersCapacity(int bookIndex, IReadOnlyList<SourceTrack> chapters, int first, int last)
        {
            var book = books[bookIndex];
            var range = chapters.Skip(first).Take(last - first + 1).ToList();
            var grouped = owner._trackPlanner.ApplyGrouping(range, grouping, book.BookTitle);
            return owner._calculator.Calculate([book with { Tracks = grouped }]);
        }
    }

    private sealed class PlanBuilder
    {
        private readonly List<PlannedCard> _cards = [];
        private readonly List<string> _unsplittable = [];
        private readonly List<string> _messages = [];

        public Dictionary<string, IReadOnlyList<SourceTrack>> SplitChapters { get; } = [];

        public void AddCard(PlannedCard card) => _cards.Add(card);

        public void Unsplittable(BookTrackPlan book, string message)
        {
            _unsplittable.Add(book.AbsLibraryItemId);
            _messages.Add(message);
        }

        public CardSetPlan Build()
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Capacity.WithinLimits) continue;
                _messages.Add($"Card {i + 1} does not fit: {string.Join(" ", _cards[i].Capacity.Messages)}");
            }

            return new CardSetPlan(
                _cards, _cards.All(c => c.Capacity.WithinLimits), _unsplittable, _messages, SplitChapters);
        }
    }
}
