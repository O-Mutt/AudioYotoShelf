using System.Globalization;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

public class CardSetService(
    AudioYotoShelfDbContext db,
    IAudiobookshelfService absService,
    ITrackPlanner trackPlanner,
    ICardCapacityCalculator calculator,
    ICardSetPlanner planner,
    ILogger<CardSetService> logger) : ICardSetService
{
    // Auto keeps short books as one track each, which is what lets a long series fit the track limit.
    private const TrackGrouping CardGrouping = TrackGrouping.Auto;
    private const int AbsFetchConcurrency = 4;
    private const int MaxNameLength = 256;

    /// <summary>A series book with its per-chapter tracks (one per file, chapter, or whole file).</summary>
    private sealed record SeriesBook(
        string Id, string Title, string? Author, string? SeriesName, float? Sequence,
        IReadOnlyList<SourceTrack> Chapters)
    {
        public BookTrackPlan Plan => new(Id, Title, Chapters);
    }

    /// <summary>
    /// What one card holds: whole books, or one part of a split book (<see cref="PartChapters"/> is
    /// then the book's full chapter list the range indexes into). Indices are positions in the
    /// series book list, used for "Books 1–4" titles when a book has no sequence number.
    /// </summary>
    private sealed record CardSpec(
        IReadOnlyList<SeriesBook> Books, int FirstIndex, int LastIndex,
        IReadOnlyList<SourceTrack>? PartChapters = null,
        int? FirstChapter = null, int? LastChapter = null, int? PartNumber = null, int? PartCount = null)
    {
        public bool IsPart => FirstChapter is not null;
    }

    // =========================================================================
    // Plan and create
    // =========================================================================

    public async Task<CardSetPlanResponse> PlanAsync(
        Guid userConnectionId, CardSetPlanRequest request, CancellationToken ct = default)
    {
        var user = await GetAbsUserAsync(userConnectionId, ct);
        var (seriesName, allBooks) = await LoadSeriesAsync(user, request.AbsLibraryId, request.AbsSeriesId, ct);
        var (books, excluded) = Exclude(allBooks, request.ExcludedItemIds);
        var plan = RunPlanner(books, request.Mode, request.Breaks, request.SplitPoints);
        return ToPlanResponse(seriesName, books, excluded, plan);
    }

    public async Task<CardSetResponse> CreateAsync(
        Guid userConnectionId, CreateCardSetRequest request, CancellationToken ct = default)
    {
        var existingId = await ExistingSetIdAsync(userConnectionId, request.AbsSeriesId, ct);
        if (existingId is not null)
            throw new CardSetConflictException("A card set already exists for this series", existingId);

        var user = await GetAbsUserAsync(userConnectionId, ct);
        var (seriesName, allBooks) = await LoadSeriesAsync(user, request.AbsLibraryId, request.AbsSeriesId, ct);
        var (books, excluded) = Exclude(allBooks, request.ExcludedItemIds);
        var plan = RunPlanner(books, request.Mode, request.Breaks, request.SplitPoints);

        var set = new CardSet
        {
            UserConnectionId = userConnectionId,
            AbsSeriesId = request.AbsSeriesId,
            AbsLibraryId = request.AbsLibraryId,
            Name = Truncate(string.IsNullOrWhiteSpace(request.Name) ? seriesName : request.Name.Trim()),
            PackingMode = request.Mode,
            ExcludedItemIds = request.ExcludedItemIds?.Distinct().ToList() ?? [],
        };

        RequireSavable(plan, () => ToPlanResponse(set.Name, books, excluded, plan));

        db.CardSets.Add(set);
        var specs = Specs(books, plan, offset: 0);
        for (var i = 0; i < specs.Count; i++)
        {
            var title = request.CardTitles?.ElementAtOrDefault(i);
            AddCard(set, specs[i], i, string.IsNullOrWhiteSpace(title) ? null : title.Trim());
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A second request for the same series got past the check above first; the unique index stopped this one.
            db.ChangeTracker.Clear();
            if (await ExistingSetIdAsync(userConnectionId, request.AbsSeriesId, ct) is { } raced)
                throw new CardSetConflictException("A card set already exists for this series", raced);
            throw;
        }
        logger.LogInformation("Created card set {CardSetId} '{Name}' with {Cards} cards for series {SeriesId}",
            set.Id, set.Name, specs.Count, set.AbsSeriesId);
        return ToResponse(set);
    }

    // =========================================================================
    // Read
    // =========================================================================

    public async Task<CardSetSummaryResponse[]> ListAsync(
        Guid userConnectionId, string? absSeriesId = null, CancellationToken ct = default)
    {
        var sets = await db.CardSets
            .Include(s => s.Cards)
            .Where(s => s.UserConnectionId == userConnectionId && (absSeriesId == null || s.AbsSeriesId == absSeriesId))
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return sets.Select(s => new CardSetSummaryResponse(
                s.Id, s.Name, s.AbsSeriesId, s.Cards.Count,
                s.Cards.Count(c => c.Status == PlaylistStatus.Transferred), StatusOf(s.Cards)))
            .ToArray();
    }

    public async Task<CardSetResponse?> GetAsync(Guid cardSetId, CancellationToken ct = default)
    {
        var set = await LoadAsync(cardSetId, ct);
        return set is null ? null : ToResponse(set);
    }

    // =========================================================================
    // Change
    // =========================================================================

    public async Task<CardSetResponse> UpdateBreaksAsync(
        Guid cardSetId, UpdateCardSetBreaksRequest request, CancellationToken ct = default)
    {
        var set = await LoadOrThrowAsync(cardSetId, ct);
        var cards = Ordered(set);
        var books = BooksOf(cards);
        var plan = RunPlanner(books, set.PackingMode, request.Breaks, request.SplitPoints ?? []);
        var specs = Specs(books, plan, offset: 0);

        var fixedCount = FixedCount(cards);
        for (var i = 0; i < fixedCount; i++)
        {
            if (i >= specs.Count || !SameContent(cards[i], specs[i]))
                throw new CardSetConflictException($"That change moves card {i + 1}, which is already on Yoto.", set.Id);
        }

        RequireSavable(plan, () => ToPlanResponse(set.Name, books, [], plan));

        ReplaceCards(set, cards, fixedCount, specs.Skip(fixedCount).ToList());
        await db.SaveChangesAsync(ct);
        return ToResponse(set);
    }

    public async Task<CardSetResponse> UpdateAsync(
        Guid cardSetId, UpdateCardSetRequest request, CancellationToken ct = default)
    {
        var set = await LoadOrThrowAsync(cardSetId, ct);

        if (!string.IsNullOrWhiteSpace(request.Name))
            set.Name = Truncate(request.Name.Trim());

        var repack = false;
        if (request.PackingMode is { } mode && mode != set.PackingMode)
        {
            set.PackingMode = mode;
            repack = true;
        }
        if (request.ExcludedItemIds is not null && !request.ExcludedItemIds.ToHashSet().SetEquals(set.ExcludedItemIds))
        {
            set.ExcludedItemIds = request.ExcludedItemIds.Distinct().ToList();
            repack = true;
        }

        if (repack)
            await RepackUnsentAsync(set, ct);

        await db.SaveChangesAsync(ct);
        return ToResponse(set);
    }

    public async Task<CardSetResponse> RenameCardAsync(
        Guid cardSetId, Guid playlistId, string name, CancellationToken ct = default)
    {
        var set = await LoadOrThrowAsync(cardSetId, ct);
        var card = set.Cards.FirstOrDefault(c => c.Id == playlistId)
            ?? throw new KeyNotFoundException("Card not found in this set");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("A card needs a name");

        card.Name = Truncate(name.Trim());
        await db.SaveChangesAsync(ct);
        return ToResponse(set);
    }

    public async Task DeleteAsync(Guid cardSetId, CancellationToken ct = default)
    {
        var set = await LoadOrThrowAsync(cardSetId, ct);
        foreach (var card in set.Cards)
        {
            card.CardSetId = null;
            card.SetIndex = null;
        }
        db.CardSets.Remove(set);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid[]> PrepareTransferAsync(
        Guid cardSetId, IReadOnlyList<Guid>? playlistIds, CancellationToken ct = default)
    {
        var set = await LoadOrThrowAsync(cardSetId, ct);
        var cards = Ordered(set);

        List<Playlist> chosen;
        if (playlistIds is null)
        {
            chosen = cards.Where(c => !IsLocked(c)).ToList();
        }
        else
        {
            var wanted = playlistIds.ToHashSet();
            chosen = cards.Where(c => wanted.Contains(c.Id)).ToList();
            if (chosen.Count != wanted.Count)
                throw new KeyNotFoundException("A card is not part of this set");
        }

        foreach (var card in chosen)
        {
            if (card.Status is PlaylistStatus.Transferring or PlaylistStatus.Queued)
                throw new CardSetConflictException($"{card.Name} is queued or being sent already.", set.Id);

            var capacity = calculator.Calculate(PlaylistCapacity.BuildPlans(card, trackPlanner));
            if (!capacity.WithinLimits)
                throw new CardSetCapacityException(
                    $"{card.Name} exceeds Yoto card capacity.", card.Id, PlaylistCapacity.ToSummary(capacity));
        }

        foreach (var card in chosen)
        {
            card.Status = PlaylistStatus.Queued;
            card.ErrorMessage = null;
        }
        await db.SaveChangesAsync(ct);
        return chosen.Select(c => c.Id).ToArray();
    }

    public async Task ReleaseQueuedAsync(IReadOnlyList<Guid> playlistIds, CancellationToken ct = default)
    {
        var queued = await db.Playlists
            .Where(p => playlistIds.Contains(p.Id) && p.Status == PlaylistStatus.Queued)
            .ToListAsync(ct);
        foreach (var card in queued)
            card.Status = PlaylistStatus.Draft;
        await db.SaveChangesAsync(ct);
    }

    // =========================================================================
    // Re-packing around cards already on Yoto
    // =========================================================================

    /// <summary>
    /// Re-packs every card after the last locked one from a fresh read of the series, with the set's
    /// mode and exclusions. Cards up to and including the last locked card keep their books; a book
    /// split across that boundary continues on the next card from the chapter after its last sent part.
    /// </summary>
    private async Task RepackUnsentAsync(CardSet set, CancellationToken ct)
    {
        var user = await GetAbsUserAsync(set.UserConnectionId, ct);
        var (_, seriesBooks) = await LoadSeriesAsync(user, set.AbsLibraryId, set.AbsSeriesId, ct);

        var cards = Ordered(set);
        var fixedCount = FixedCount(cards);
        var fixedItems = cards.Take(fixedCount).SelectMany(c => c.Items).ToList();
        var placed = fixedItems.Where(i => !i.IsPart).Select(i => i.AbsLibraryItemId).ToHashSet();

        var tail = new List<SeriesBook>();
        (int Offset, int PartsSent, IReadOnlyList<SourceTrack> Chapters)? remainder = null;
        foreach (var parts in fixedItems.Where(i => i.IsPart).GroupBy(i => i.AbsLibraryItemId))
        {
            placed.Add(parts.Key);
            var chapters = AllChapters(parts.First());
            var next = parts.Max(i => i.LastChapterIndex!.Value) + 1;
            if (next >= chapters.Count || remainder is not null) continue;

            remainder = (next, parts.Max(i => i.PartNumber ?? 0), chapters);
            var item = parts.First();
            tail.Add(new SeriesBook(item.AbsLibraryItemId, item.BookTitle, item.BookAuthor, item.SeriesName,
                item.SeriesSequence, chapters.Skip(next).ToList()));
        }

        tail.AddRange(seriesBooks.Where(b => !placed.Contains(b.Id) && !set.ExcludedItemIds.Contains(b.Id)));

        var plan = planner.Plan(tail.Select(b => b.Plan).ToList(), set.PackingMode, CardGrouping,
            remainder is null ? null : new HashSet<int> { 0 });
        RequireSavable(plan, () => ToPlanResponse(set.Name, tail, [], plan));

        // The continued split book is already counted in `placed`, and it is also tail[0].
        var specs = Specs(tail, plan, offset: remainder is null ? placed.Count : placed.Count - 1);

        if (remainder is { } r)
        {
            var continued = placed.Count - 1;
            specs = specs.Select(s => s.FirstIndex != continued ? s : s.IsPart
                    ? s with
                    {
                        PartChapters = r.Chapters,
                        FirstChapter = r.Offset + s.FirstChapter,
                        LastChapter = r.Offset + s.LastChapter,
                        PartNumber = r.PartsSent + s.PartNumber,
                        PartCount = r.PartsSent + s.PartCount,
                    }
                    : s with
                    {
                        PartChapters = r.Chapters,
                        FirstChapter = r.Offset,
                        LastChapter = r.Chapters.Count - 1,
                        PartNumber = r.PartsSent + 1,
                        PartCount = r.PartsSent + 1,
                    })
                .ToList();
        }

        ReplaceCards(set, cards, fixedCount, specs);
    }

    /// <summary>
    /// Rewrites the cards from <paramref name="fixedCount"/> on. A card whose books and range are
    /// unchanged keeps its title and state; a changed card gets a default title and goes back to draft.
    /// </summary>
    private void ReplaceCards(CardSet set, List<Playlist> cards, int fixedCount, IReadOnlyList<CardSpec> specs)
    {
        for (var j = 0; j < specs.Count; j++)
        {
            var index = fixedCount + j;
            if (index >= cards.Count)
            {
                AddCard(set, specs[j], index, title: null);
                continue;
            }

            var card = cards[index];
            card.SetIndex = index;
            if (SameContent(card, specs[j])) continue;

            db.PlaylistItems.RemoveRange(card.Items);
            card.Items.Clear();
            foreach (var item in BuildItems(specs[j]))
            {
                // Added explicitly: through the tracked collection alone, EF takes an item with a
                // preset key for an existing row and tries to update it.
                item.Playlist = card;
                db.PlaylistItems.Add(item);
            }
            card.Name = DefaultTitle(set.Name, specs[j]);
            card.Status = PlaylistStatus.Draft;
            card.ErrorMessage = null;
        }

        foreach (var extra in cards.Skip(fixedCount + specs.Count))
        {
            set.Cards.Remove(extra);
            db.Playlists.Remove(extra);
        }
    }

    // =========================================================================
    // Building cards
    // =========================================================================

    private CardSetPlan RunPlanner(
        IReadOnlyList<SeriesBook> books, PackingMode mode, int[]? breaks, Dictionary<string, int[]>? splitPoints)
    {
        var plans = books.Select(b => b.Plan).ToList();
        try
        {
            if (breaks is null && splitPoints is null)
                return planner.Plan(plans, mode, CardGrouping);

            var points = (splitPoints ?? []).ToDictionary(p => p.Key, p => (IReadOnlyList<int>)p.Value);
            return planner.Apply(plans, breaks ?? [], points, mode, CardGrouping);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static List<CardSpec> Specs(IReadOnlyList<SeriesBook> books, CardSetPlan plan, int offset) =>
        plan.Cards.Select(c => c.IsPart
                ? new CardSpec([books[c.FirstBookIndex]], offset + c.FirstBookIndex, offset + c.LastBookIndex,
                    plan.SplitChapters[books[c.FirstBookIndex].Id],
                    c.FirstChapterIndex, c.LastChapterIndex, c.PartNumber, c.PartCount)
                : new CardSpec(books.Skip(c.FirstBookIndex).Take(c.LastBookIndex - c.FirstBookIndex + 1).ToList(),
                    offset + c.FirstBookIndex, offset + c.LastBookIndex))
            .ToList();

    private void AddCard(CardSet set, CardSpec spec, int index, string? title)
    {
        var card = new Playlist
        {
            UserConnectionId = set.UserConnectionId,
            Name = Truncate(title ?? DefaultTitle(set.Name, spec)),
            DefaultGrouping = CardGrouping,
            CardSet = set,
            SetIndex = index,
        };
        foreach (var item in BuildItems(spec))
            card.Items.Add(item);
        set.Cards.Add(card);
        db.Playlists.Add(card);
    }

    private static IEnumerable<PlaylistItem> BuildItems(CardSpec spec) =>
        spec.Books.Select((book, position) =>
        {
            var chapters = spec.PartChapters ?? book.Chapters;
            return new PlaylistItem
            {
                AbsLibraryItemId = book.Id,
                Position = position,
                BookTitle = book.Title,
                BookAuthor = book.Author,
                SeriesName = book.SeriesName,
                SeriesSequence = book.Sequence,
                TrackDurations = chapters.Select(c => c.DurationSeconds).ToList(),
                TrackBytes = chapters.Select(c => c.EstimatedBytes).ToList(),
                FirstChapterIndex = spec.FirstChapter,
                LastChapterIndex = spec.LastChapter,
                PartNumber = spec.PartNumber,
                PartCount = spec.PartCount,
            };
        });

    /// <summary>Spec §4: "Series · Books 1–4", "Series 3", or "Series 4 · Part 1 of 2".</summary>
    private static string DefaultTitle(string setName, CardSpec spec)
    {
        string Number(SeriesBook book, int index) =>
            book.Sequence?.ToString("0.##", CultureInfo.InvariantCulture) ?? (index + 1).ToString(CultureInfo.InvariantCulture);
        string OneBook(SeriesBook book, int index) =>
            book.Sequence is null ? book.Title : $"{setName} {Number(book, index)}";

        var title = spec.IsPart
            ? $"{OneBook(spec.Books[0], spec.FirstIndex)} · Part {spec.PartNumber} of {spec.PartCount}"
            : spec.Books.Count == 1
                ? OneBook(spec.Books[0], spec.FirstIndex)
                : $"{setName} · Books {Number(spec.Books[0], spec.FirstIndex)}–{Number(spec.Books[^1], spec.LastIndex)}";
        return Truncate(title);
    }

    private static bool SameContent(Playlist card, CardSpec spec)
    {
        var items = card.Items.OrderBy(i => i.Position).ToList();
        if (items.Count != spec.Books.Count) return false;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].AbsLibraryItemId != spec.Books[i].Id) return false;
        }
        var first = items.FirstOrDefault();
        return first?.FirstChapterIndex == spec.FirstChapter && first?.LastChapterIndex == spec.LastChapter;
    }

    // =========================================================================
    // Responses
    // =========================================================================

    private CardSetPlanResponse ToPlanResponse(
        string seriesName, IReadOnlyList<SeriesBook> books, IReadOnlyList<SeriesBook> excluded, CardSetPlan plan)
    {
        var specs = Specs(books, plan, offset: 0);
        var cards = plan.Cards.Select((c, i) => new CardSetCardResponse(
                i, c.FirstBookIndex, c.LastBookIndex, c.FirstChapterIndex, c.LastChapterIndex,
                c.PartNumber, c.PartCount, DefaultTitle(seriesName, specs[i]), PlaylistCapacity.ToSummary(c.Capacity)))
            .ToArray();

        return new CardSetPlanResponse(
            seriesName,
            books.Select(b => BookResponse(b, plan.SplitChapters.GetValueOrDefault(b.Id))).ToArray(),
            excluded.Select(b => new ExcludedBookResponse(b.Id, b.Title, b.Sequence)).ToArray(),
            cards,
            plan.WithinLimits,
            plan.UnsplittableBookIds.ToArray(),
            plan.Messages.ToArray());
    }

    private CardSetResponse ToResponse(CardSet set)
    {
        var cards = Ordered(set);
        var books = BooksOf(cards);
        var bookIndex = books.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var splitIds = cards.SelectMany(c => c.Items).Where(i => i.IsPart).Select(i => i.AbsLibraryItemId).ToHashSet();

        var messages = new List<string>();
        var cardResponses = cards.Select((card, index) =>
        {
            var items = card.Items.OrderBy(i => i.Position).ToList();
            var part = items.FirstOrDefault(i => i.IsPart);
            var capacity = calculator.Calculate(PlaylistCapacity.BuildPlans(card, trackPlanner));
            if (!capacity.WithinLimits)
                messages.Add($"Card {index + 1} does not fit: {string.Join(" ", capacity.Messages)}");

            return new CardSetCardResponse(
                index,
                items.Count == 0 ? -1 : bookIndex[items[0].AbsLibraryItemId],
                items.Count == 0 ? -1 : bookIndex[items[^1].AbsLibraryItemId],
                part?.FirstChapterIndex, part?.LastChapterIndex, part?.PartNumber, part?.PartCount,
                card.Name, PlaylistCapacity.ToSummary(capacity),
                card.Id, card.Status, IsLocked(card), card.YotoCardId, card.ErrorMessage);
        }).ToArray();

        return new CardSetResponse(
            set.Id, set.Name, set.AbsSeriesId, set.AbsLibraryId, set.PackingMode,
            set.ExcludedItemIds.ToArray(), StatusOf(cards),
            books.Select(b => BookResponse(b, splitIds.Contains(b.Id) ? b.Chapters : null)).ToArray(),
            cardResponses,
            messages.Count == 0,
            messages.ToArray());
    }

    private CardSetBookResponse BookResponse(SeriesBook book, IReadOnlyList<SourceTrack>? splitChapters)
    {
        var grouped = trackPlanner.ApplyGrouping(book.Chapters, CardGrouping, book.Title);
        var bytes = calculator.Calculate([book.Plan with { Tracks = grouped }]).Projected.Bytes;
        return new CardSetBookResponse(
            book.Id, book.Title, book.Sequence, book.Chapters.Sum(c => c.DurationSeconds), bytes,
            splitChapters is not null,
            splitChapters?.Select((c, i) => new CardSetChapterResponse(i, c.Title, c.DurationSeconds)).ToArray());
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// The set's books in card order, from the cards' cached track data (no Audiobookshelf calls).
    /// A split book appears once, with the full chapter list its parts index into.
    /// </summary>
    private static List<SeriesBook> BooksOf(IEnumerable<Playlist> cards)
    {
        var books = new List<SeriesBook>();
        var seen = new HashSet<string>();
        foreach (var item in cards.SelectMany(c => c.Items.OrderBy(i => i.Position)))
        {
            if (!seen.Add(item.AbsLibraryItemId)) continue;
            books.Add(new SeriesBook(item.AbsLibraryItemId, item.BookTitle, item.BookAuthor, item.SeriesName,
                item.SeriesSequence, AllChapters(item)));
        }
        return books;
    }

    private static List<SourceTrack> AllChapters(PlaylistItem item) =>
        item.TrackDurations
            .Select((duration, i) => new SourceTrack(
                $"Chapter {i + 1}", duration, i < item.TrackBytes.Count ? item.TrackBytes[i] : 0))
            .ToList();

    private static (List<SeriesBook> Included, List<SeriesBook> Excluded) Exclude(
        IReadOnlyList<SeriesBook> books, string[]? excludedIds)
    {
        var excluded = excludedIds?.ToHashSet() ?? [];
        return (books.Where(b => !excluded.Contains(b.Id)).ToList(),
                books.Where(b => excluded.Contains(b.Id)).ToList());
    }

    /// <summary>
    /// Reads the series and each book's full media. Books are ordered by sequence number (books
    /// without one last), then title, so fractional sequences such as 1.5 sort in place.
    /// </summary>
    private async Task<(string Name, List<SeriesBook> Books)> LoadSeriesAsync(
        UserConnection user, string libraryId, string seriesId, CancellationToken ct)
    {
        var series = await absService.GetSeriesDetailAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, libraryId, seriesId, ct);

        var ordered = series.Books
            .Select(b => (b.Id, Sequence: PlaylistService.ParseSequence(b.Sequence), Title: b.Media?.Metadata.Title ?? ""))
            .OrderBy(b => b.Sequence is null)
            .ThenBy(b => b.Sequence)
            .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fetched = new SeriesBook?[ordered.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, ordered.Count),
            new ParallelOptions { MaxDegreeOfParallelism = AbsFetchConcurrency, CancellationToken = ct },
            async (i, token) =>
            {
                var item = await absService.GetLibraryItemAsync(
                    user.AudiobookshelfUrl, user.AudiobookshelfToken!, ordered[i].Id, token);
                if (item.Media is not { } media)
                {
                    logger.LogWarning("Series {SeriesId}: skipping book {ItemId} with no media", seriesId, ordered[i].Id);
                    return;
                }
                var metadata = media.Metadata;
                fetched[i] = new SeriesBook(
                    ordered[i].Id,
                    metadata.Title ?? "Unknown",
                    metadata.Authors?.FirstOrDefault()?.Name,
                    metadata.Series?.FirstOrDefault(s => s.Id == seriesId)?.Name ?? series.Name,
                    ordered[i].Sequence,
                    trackPlanner.Plan(media, TrackGrouping.Chapters));
            });

        return (series.Name, fetched.OfType<SeriesBook>().ToList());
    }

    private async Task<CardSet?> LoadAsync(Guid cardSetId, CancellationToken ct) =>
        await db.CardSets
            .Include(s => s.Cards).ThenInclude(c => c.Items)
            .FirstOrDefaultAsync(s => s.Id == cardSetId, ct);

    private async Task<CardSet> LoadOrThrowAsync(Guid cardSetId, CancellationToken ct) =>
        await LoadAsync(cardSetId, ct) ?? throw new KeyNotFoundException("Card set not found");

    private async Task<UserConnection> GetAbsUserAsync(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct)
            ?? throw new InvalidOperationException("User connection not found");
        if (!user.HasValidAbsConnection)
            throw new InvalidOperationException("No valid Audiobookshelf connection");
        await AbsTokens.EnsureValidAsync(db, absService, user, logger, ct);
        return user;
    }

    /// <summary>
    /// Refuses a plan with a card that does not fit, or one that leaves out a book it cannot split:
    /// saving it would drop that book from the set without a trace, so it has to be excluded first.
    /// </summary>
    private static void RequireSavable(CardSetPlan plan, Func<CardSetPlanResponse> response)
    {
        if (plan.UnsplittableBookIds.Count > 0)
            throw new CardSetPlanException(
                "A book has a chapter longer than one card holds; leave it out of the plan first.", response());
        if (!plan.WithinLimits)
            throw new CardSetPlanException("A card in this plan does not fit.", response());
    }

    private async Task<Guid?> ExistingSetIdAsync(Guid userConnectionId, string absSeriesId, CancellationToken ct) =>
        await db.CardSets
            .Where(s => s.UserConnectionId == userConnectionId && s.AbsSeriesId == absSeriesId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

    private static List<Playlist> Ordered(CardSet set) =>
        set.Cards.OrderBy(c => c.SetIndex ?? int.MaxValue).ToList();

    /// <summary>A card on Yoto, queued to send or being sent never changes.</summary>
    private static bool IsLocked(Playlist card) =>
        card.Status is PlaylistStatus.Transferred or PlaylistStatus.Transferring or PlaylistStatus.Queued;

    /// <summary>Cards up to the last locked one stay as they are, so nothing slides under a sent card.</summary>
    private static int FixedCount(List<Playlist> cards) => cards.FindLastIndex(IsLocked) + 1;

    private static CardSetStatus StatusOf(IEnumerable<Playlist> cards)
    {
        var statuses = cards.Select(c => c.Status).ToList();
        if (statuses.Contains(PlaylistStatus.Transferring) || statuses.Contains(PlaylistStatus.Queued))
            return CardSetStatus.Sending;
        if (statuses.Contains(PlaylistStatus.Failed)) return CardSetStatus.NeedsAttention;
        if (statuses.Count > 0 && statuses.All(s => s == PlaylistStatus.Transferred)) return CardSetStatus.Complete;
        return CardSetStatus.Draft;
    }

    private static string Truncate(string value) =>
        value.Length <= MaxNameLength ? value : value[..MaxNameLength];
}
