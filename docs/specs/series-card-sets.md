# Spec: series card sets

**Status:** Built, Phases 1 and 2 ([#114](https://github.com/O-Mutt/AudioYotoShelf/issues/114)). §13 lists where the build differs from this design.
**Scope:** turn an Audiobookshelf series into an ordered set of Yoto Make Your Own (MYO) cards,
each holding a run of whole books, packed under the card limits.

Related: [yoto-card-limits.md](yoto-card-limits.md) (what a card can hold, and how that was measured).

## 1. Goals and non-goals

**Goals**

1. From a series page, make the fewest MYO cards that hold the whole series, in series order.
2. Never split a book across cards when it fits on one card. Each card holds books `a..b`, the next
   card starts at `b+1`.
3. Split a book that is too big for one card at chapter boundaries, onto cards of its own.
4. Show the plan before anything is sent: which books land on which card and how full each card is.
5. Let the user move a break between two cards, as long as every card still fits.
6. Keep working when the series grows: new books join the last unsent card or a new card. Cards
   already on Yoto never change.
7. Show card status on the series grid ("On 3 cards", "1 new book").

**Non-goals**

- Packing books from different series onto one card. That is a plain playlist.
- Reordering books. Series order comes from Audiobookshelf's sequence numbers.
- Changing what a Yoto card holds after it has been sent, other than re-sending the same card.
- Enforcing a card limit on single-book transfers. That was a separate gap (§11, [#117](https://github.com/O-Mutt/AudioYotoShelf/issues/117)).

## 2. Current state

- **Series display exists.** `LibraryView.vue` has a Series tab; `SeriesDetailView.vue` lists the
  books and has "Transfer Entire Series".
- **"Transfer Entire Series" makes one card per book** (`TransferOrchestrator.TransferSeriesAsync`).
  `CreateSeriesTransferRequest.OneCardPerBook` is accepted and ignored.
- **The multi-book, one-card backend exists, with no UI.** `Playlist` / `PlaylistItem`,
  `PlaylistService` (`AddSeriesAsync` adds a whole series), `PlaylistTransferOrchestrator` (one Yoto
  chapter per book, tracks inside it), and the capacity gate in `CardCapacityCalculator`.
- **Card limits are config-driven** (`YotoCardLimits`). As of this spec the binding limit is about
  500 MB of Yoto's stored audio, estimated at 100 kbps, which is about 11.6 hours per card.

## 3. Design summary

A **card set** is an ordered list of playlists, one per card, made from one series. A pure planner
packs the series' books into cards using the existing capacity calculator, so every limit (size,
tracks, optional duration) applies and **the design does not depend on the numbers**. If Yoto's
enforced limits turn out higher or lower, only `YotoCardLimits` changes and plans re-pack.

Sending a set sends each card with the existing playlist transfer.

Examples at the current limits (about 11.6 h per card):

| Series | Plan |
|---|---|
| Dragon Masters 1–33 (~32 h) | 3 cards of about 11 whole books each |
| Harry Potter 1–3 | 3 cards, one book each |
| Frog and Toad (~2 h) | 1 card |
| Harry Potter 4, Goblet of Fire (~20 h, one book) | 2 cards: chapters split between them (§5.4) |

## 4. Concepts and naming

- **Card set**: one series, its book list, a packing mode, and its cards.
- **Card**: a `Playlist` with `CardSetId` and `SetIndex` (0-based). Card *n* holds either a
  contiguous run of whole books or one part of a split book.
- **Split book**: a book too big for one card. It is cut at chapter boundaries into parts, one per
  card, and none of its cards holds any other book.
- **Chapter**: an entry in the book's per-chapter track list, the list `ITrackPlanner.Plan(media,
  TrackGrouping.Chapters)` returns and `PlaylistItem.TrackDurations` caches (one entry per audio file
  for multi-file books, per embedded chapter for single-file books).
- **Break**: the boundary between card *n* and card *n+1*, stored as the index of the first book on
  card *n+1*.
- **Locked card**: a card whose playlist status is `Transferred`. Its books and breaks never move.

Default card titles (editable per card):

| Card holds | Title |
|---|---|
| books *a..b* of the series | `{Series} · Books {a}–{b}` |
| one book | `{Series} {n}` (for example `Harry Potter 3`) |
| part *i* of *k* of one book (§5.4) | `{Series} {n} · Part {i} of {k}` |

`{a}`, `{b}` and `{n}` are the Audiobookshelf sequence numbers, not positions, so a series with
gaps (1, 2, 4) reads correctly.

## 5. Planner

### 5.1 Inputs and output

```csharp
// Core/Interfaces/ICardSetPlanner.cs
public interface ICardSetPlanner
{
    CardSetPlan Plan(IReadOnlyList<BookTrackPlan> books, PackingMode mode);

    // Validates user-chosen book breaks and split points; returns per-card capacity so the UI can
    // show which card overflows.
    CardSetPlan Apply(IReadOnlyList<BookTrackPlan> books, IReadOnlyList<int> breaks,
                      IReadOnlyDictionary<string, IReadOnlyList<int>> splitPoints);
}

public enum PackingMode { Balance, Fill }

// Whole books: the chapter range and part numbers are null.
// One part of a split book: FirstBookIndex == LastBookIndex and the chapter range is set.
public record PlannedCard(
    int FirstBookIndex, int LastBookIndex,
    int? FirstChapterIndex, int? LastChapterIndex,
    int? PartNumber, int? PartCount,
    CapacityResult Capacity);

public record CardSetPlan(
    IReadOnlyList<PlannedCard> Cards,
    bool WithinLimits,                         // every card fits
    IReadOnlyList<string> UnsplittableBookIds, // a single chapter is bigger than a card (§5.4)
    IReadOnlyList<string> Messages);
```

`BookTrackPlan` is the existing type, built from cached `PlaylistItem` track data by
`PlaylistCapacity.BuildPlans`, so planning makes no Audiobookshelf calls. The planner lives in Core,
has no I/O, and decides "fits" only by calling `ICardCapacityCalculator.Calculate` on a run of books.

### 5.2 Fill

Walk the books in order. Add each book to the current card; if the card no longer fits, close it and
start the next card with that book. This gives the minimum number of cards for contiguous runs
(greedy is optimal for one-dimensional contiguous packing, and every limit here is additive).

### 5.3 Balance (default)

Use Fill's card count *k*, then choose breaks that minimise the fullest card. "Fullness" of a run is
the largest of `tracks / MaxTracks`, `bytes / MaxTotalBytes` and, when set,
`duration / MaxTotalDurationSeconds`.

Dynamic programme over book index *i* and card count *c*:
`best[i][c] = min over j < i of max(best[j][c-1], fullness(j..i-1))`, keeping the argmin to rebuild
the breaks. Series are at most a few hundred books, so O(n²·k) is instant. Ties go to the later break,
so earlier cards come out fuller and the last card has the most room for future books.

### 5.4 Books larger than one card

A book that does not fit on a card alone (Harry Potter 4–7, Wings of Fire omnibuses) is split.

1. **Segment the series.** Oversized books cut the book list into runs. Each run of normal books is
   packed on its own with §5.2 / §5.3. An oversized book never shares a card with a neighbour, so
   the book before it ends one card and the book after it starts the next.
2. **Split the book.** Treat its chapters as the items and pack them with the same Fill / Balance
   code (§5.2, §5.3): Fill gives the number of parts *k*, Balance places the *k−1* split points so
   the parts are as even as possible. Only chapter boundaries are candidates, so no chapter is cut.
3. **Books without chapters.** A single-file book with no embedded chapters has one entry in its
   chapter list. The planner first divides it into equal segments no longer than
   `MaxTrackDurationSeconds` (the same cut the transfer already makes for oversized tracks) and
   treats those as its chapters; segment *i* covers `[i·len, (i+1)·len)` seconds.
4. **Unsplittable.** If one chapter alone is bigger than a card, the book is returned in
   `UnsplittableBookIds` and left out of the plan, with a message naming it. Not expected at
   current limits (a card holds about 11.6 h).

Goblet of Fire with its real chapters (20 h 13 m, about 868 MB stored at the 100 kbps estimate)
becomes 2 parts, split after Chapter 21: about 441 MB and 427 MB.

On Yoto each part is one card holding one Yoto chapter (the book) whose tracks are the chapters in
its range. Track numbers restart on each card; track titles keep the real chapter names.

### 5.5 Breaks chosen by the user

`Apply` takes book break indices and, per split book, its split points (the index of the first
chapter on each part after the first) from the UI. It returns the same `CardSetPlan` shape, with
`WithinLimits = false` and the overflowing card flagged when a break or split point makes a card
too big. The UI refuses to save an invalid plan. Breaks or split points inside or before a locked
card are rejected.

## 6. Data model

New entity:

```csharp
public class CardSet : BaseEntity
{
    public Guid UserConnectionId { get; set; }
    public UserConnection UserConnection { get; set; } = null!;

    public required string AbsSeriesId { get; set; }
    public required string AbsLibraryId { get; set; }
    public required string Name { get; set; }          // defaults to the series name
    public PackingMode PackingMode { get; set; } = PackingMode.Balance;

    /// <summary>Series books the user left out, such as a second edition of the same book.</summary>
    public List<string> ExcludedItemIds { get; set; } = [];

    public ICollection<Playlist> Cards { get; set; } = [];
}
```

Changes to existing entities:

```csharp
// Playlist
public Guid? CardSetId { get; set; }
public CardSet? CardSet { get; set; }
public int? SetIndex { get; set; }

// PlaylistItem: set only for one part of a split book; null = whole book.
// Indices into the book's chapter list (§4), inclusive.
public int? FirstChapterIndex { get; set; }
public int? LastChapterIndex { get; set; }
public int? PartNumber { get; set; }
public int? PartCount { get; set; }
```

The set's status is derived from its cards: all `Transferred` → complete; any `Transferring` →
sending; any `Failed` → needs attention; otherwise draft.

**Migration `AddCardSets`**

- Create table `CardSets` (`Id`, `UserConnectionId` FK → `UserConnections` ON DELETE CASCADE,
  `AbsSeriesId` varchar(256), `AbsLibraryId` varchar(256), `Name` varchar(1024),
  `PackingMode` varchar(32), `ExcludedItemIds` text[], `CreatedAt`, `UpdatedAt`).
- Unique index `(UserConnectionId, AbsSeriesId)`: one set per series per user.
- `Playlists`: add nullable `CardSetId` (FK → `CardSets` ON DELETE SET NULL) and `SetIndex` int;
  index `(CardSetId, SetIndex)`.
- `PlaylistItems`: add nullable `FirstChapterIndex`, `LastChapterIndex`, `PartNumber`,
  `PartCount` int.
- All new columns are nullable or have defaults; existing playlists are untouched. No data migration.

Deleting a set leaves its playlists as standalone playlists (SET NULL). Nothing on Yoto is deleted.

**Changes to existing code for split parts**

- `PlaylistCapacity.ChapterTracks` / `BuildPlans`: use only the chapters in the item's range.
- `PlaylistTransferOrchestrator.BuildBookChapterAsync`: download or extract only the chapters in the
  range (for multi-file books, only the files in the range). The Yoto chapter title becomes
  `"{Book} · Part {i} of {k}"`.
- `PlaylistService.AddItemsAsync` dedupes by `AbsLibraryItemId` within a playlist. A split book's
  parts are in different playlists, so this still holds.

## 7. API

All routes under `/api/cardsets`, scoped to the signed-in user like `PlaylistsController`
(404 for another user's set).

### 7.1 Preview a plan (no writes)

`POST /api/cardsets/plan`

```json
{ "absSeriesId": "ser_123", "absLibraryId": "lib_1", "mode": "Balance",
  "excludedItemIds": [], "breaks": null }
```

`breaks` is optional; when present the server calls `Apply` instead of `Plan`.

`200`:

```json
{
  "seriesName": "Dragon Masters",
  "books": [ { "absLibraryItemId": "li_1", "title": "Rise of the Earth Dragon", "sequence": 1,
               "durationSeconds": 2880, "estimatedBytes": 36000000, "split": false,
               "chapters": null } ],
  "cards": [ { "index": 0, "firstBookIndex": 0, "lastBookIndex": 10,
               "firstChapterIndex": null, "lastChapterIndex": null, "partNumber": null, "partCount": null,
               "title": "Dragon Masters · Books 1–11",
               "capacity": { "tracks": 14, "maxTracks": 100, "durationSeconds": 36840,
                             "maxDurationSeconds": null, "bytes": 460500000, "maxBytes": 524288000,
                             "withinLimits": true, "messages": [] } } ],
  "withinLimits": true,
  "unsplittableBookIds": [],
  "messages": []
}
```

`capacity` is the existing `CapacitySummary`. For a split book, `"split": true` and `"chapters"`
lists `{ "index", "title", "durationSeconds" }` so the UI can draw the split editor; its cards carry
the chapter range and part numbers.

The request also takes optional `"splitPoints": { "<absLibraryItemId>": [22] }` alongside
`"breaks"`.

### 7.2 Create a set

`POST /api/cardsets` with the same body plus optional `"name"` and `"cardTitles": ["…"]`.
Creates the `CardSet` and one draft `Playlist` per card, adding books with the existing
`AddItemsAsync` caching. `201` with the set (shape in §7.3).
`409 { "message": "A card set already exists for this series", "cardSetId": "…" }` if one exists.
`422 { "message": "…", "plan": { … } }` if the given breaks do not fit.

### 7.3 Read

- `GET /api/cardsets` → `[{ "id", "name", "absSeriesId", "cardCount", "sentCount", "status" }]`
- `GET /api/cardsets/{id}` →
  `{ "id", "name", "absSeriesId", "absLibraryId", "packingMode", "excludedItemIds",
     "cards": [ PlaylistResponse + "setIndex" + "locked" ], "newBookIds": [] }`
  `newBookIds` lists series books in Audiobookshelf that are in no card and not excluded.
- `GET /api/cardsets/status?seriesIds=a,b,c` →
  `[{ "absSeriesId", "cardSetId", "cardCount", "sentCount", "sending": false, "newBookCount": 1 }]`
  for the series grid badges, in one call per page.
- `GET /api/cardsets/sends?status=Sending` → the caller's sets with at least one card ever queued or
  sent (any card not `Draft`), most recent activity first, for the Transfers page:
  `[{ "id", "name", "absSeriesId", "absLibraryId", "status", "cardCount", "sentCount",
      "lastActivityAt", "cards": [{ "playlistId", "index", "title", "status", "yotoCardId",
      "errorMessage", "transferredAt", "bookTitles": [] }] }]`.
  `lastActivityAt` is the latest `UpdatedAt` of the set's cards that are not drafts. `status` is
  optional and filters on the set status.

### 7.4 Change

- `PUT /api/cardsets/{id}/breaks` `{ "breaks": [11, 22], "splitPoints": { "li_hp4": [22] } }` →
  re-assigns books and chapters among **unlocked** cards, adding or removing unlocked cards as
  needed. `422` with the plan if a card would not fit; `409` if a break or split point touches a
  locked card.
- `PATCH /api/cardsets/{id}` `{ "name"?, "packingMode"?, "excludedItemIds"? }` → re-packs unlocked
  cards when the mode or exclusions change.
- `PATCH /api/cardsets/{id}/cards/{playlistId}` `{ "name" }` → card title (reuses
  `UpdatePlaylistRequest`).
- `POST /api/cardsets/{id}/refresh` → re-reads the series, then places `newBookIds`: they fill the
  last unlocked card and any new cards after it, packed with the set's mode. Locked cards stay as
  they are, even if a new book's sequence number falls between them; such a book goes into the
  first unlocked card after it and the response lists it in `"messages"`.
- `DELETE /api/cardsets/{id}` → removes the set; playlists stay (§6).

### 7.5 Send

`POST /api/cardsets/{id}/transfer` `{ "playlistIds": null }` (null = every unsent card).
Enqueues one Hangfire job, `ICardSetJobService.ExecuteCardSetTransferAsync(cardSetId, playlistIds)`,
which sends the cards one after another through `PlaylistTransferOrchestrator.TransferPlaylistAsync`.
A failed card is marked `Failed` and the job moves on to the next card; the user retries a failed
card with the same endpoint and its id. `202 { "cardSetId", "jobId" }`.
`409` if any requested card is over capacity (same body as the playlist transfer 409).

Progress uses the existing SignalR hub, adding the set id and card index to each update.

## 8. UI

Mockups: the "Series MYO Cards" canvas (C1–C3). Built in Vue with a new `cardSetStore` (Pinia) and
`cardSetApi` in `services/api.ts`.

- **Series page (`SeriesDetailView.vue`)**: a "Card plan" panel above the book list.
  - One tile per card: card *n* of *k*, cover strip, editable title, size bar
    ("≈ 439 MB of 500 MB" for the §7.1 example's 460,500,000 bytes; MB here is MiB, as in the
    calculator's messages), book and track counts.
  - Balance / Fill toggle, re-planning on change through `POST /plan`.
  - Book list grouped by card with a draggable break between groups. Dropping a break calls
    `POST /plan` with `breaks`; an invalid drop snaps back and shows which card overflowed.
  - Primary button "Make *k* cards" (create + send). "One card per book" stays as a secondary action.
  - Sent cards show "On Yoto" and their break handles are disabled.
  - A split book shows its parts as consecutive tiles ("Harry Potter 4 · Part 1 of 2") with a
    chapter-range label.
- **Series grid (`LibraryView.vue`)**: one badge per series tile from `GET /status`:
  "On *k* cards", "Sending card *i* of *k*", "*n* new books", "Not on cards", "Fits on 1 card".
  Filter chips: All / Not on cards / New books since last cards.
- **Transfers page (`TransfersView.vue`)**: a "Series card sets" section above the book transfers,
  from `GET /sends`, hidden when there are none. One row per set: name, "*n* of *k* cards sent", the
  set status, when it was last active, and a link to the series page. A row opens to list each card
  with its status, its books, its Yoto card id once sent and its error when it failed. Retry and
  cancel stay on the series page. The page joins `cardset:{id}` for each set that is sending and
  updates a card in place on each push; it re-reads the sets on `TransferListChanged`, which the
  send raises when it is queued, when the job starts and when it ends. The status filter maps onto
  set status (Completed → Complete, Failed → NeedsAttention, any in-progress status → Sending,
  Cancelled → none). "Clear completed" clears book transfers only; card sets are not records to
  clear, and the set lives on in the series page.
- **Split editor**: for a split book, a chapter strip (widths by length) with the split points;
  clicking a chapter or using the arrows moves a split point one chapter. Shown inline on the series
  page under the card tiles. An invalid split keeps "Make cards" disabled and names the card that
  overflows.

## 9. Edge cases

- **Missing or fractional sequence numbers.** Sort by sequence, then title. Books without a sequence go
  last. Fractional sequences (1.5 novellas) sort in place.
- **Duplicate editions.** A series can hold two editions of one book (this library has *Chamber of
  Secrets* and its Full-Cast Edition). The plan shows both; the user excludes one, stored in
  `ExcludedItemIds`. Same-sequence books get a "duplicate?" hint.
- **Series across libraries.** Use the library `GetSeriesDetail` resolves, as the series page does
  today.
- **A book with no chapters that is too big for one card.** Split by time into equal segments
  (§5.4 step 3); the transfer extracts each segment with `IChapterExtractor.ExtractChapterAsync`.
- **A split book whose chapters change in Audiobookshelf** before its parts are sent. Re-plan the
  unsent parts; sent parts stay as they are.
- **Audiobookshelf metadata changes after planning.** Unlocked cards re-read their books when sent
  (the playlist transfer already does), and capacity is re-checked before sending.
- **The Yoto card was deleted.** The existing create-or-update path makes a new card; the set keeps
  working.
- **A sent card is edited in Audiobookshelf later** (a chapter fix). Out of scope; the user can resend
  that card.

## 10. Phases

1. **Card sets.** Planner (Fill, Balance, Apply, splitting oversized books), `CardSet` entity and
   migration including the chapter range on `PlaylistItem`, chapter-range support in the playlist
   capacity and transfer, §7 endpoints except refresh, series page panel with the split editor, send.
2. **Growth and grid.** `refresh`, `newBookIds`, series grid badges and filters.

## 11. Open questions

1. **Real Yoto limits.** A ~20 h *Goblet of Fire* card (about 565 MB stored) is being sent to see
   whether Yoto enforces 500 MB at all. The answer changes `YotoCardLimits` defaults only.
2. **Cover art for multi-book cards.** The playlist transfer only sets a cover for a single-book
   playlist. Options: first book's cover, or a generated series cover.
3. **Single-book transfers have no capacity check.** Resolved by [#117](https://github.com/O-Mutt/AudioYotoShelf/issues/117): "One card per book"
   sends the books that fit and reports the rest as failed with the reason; see
   [Yoto card limits](yoto-card-limits.md#single-book-transfers).

## 12. Test plan

- **Planner unit tests (Core).**
  - Fill: empty series; one book; all books fit one card; exact-fit boundary; one byte over.
  - Balance: same card count as Fill; never fuller than Fill's fullest card; ties break later;
    the track limit binds before size.
  - Apply: valid breaks; a break that overflows a card; breaks out of order or out of range.
  - Split: a book over one card splits into the fewest parts; split points fall only on chapter
    boundaries; parts are balanced; neighbouring books never share a split book's cards; the runs
    of normal books on each side pack independently.
  - Split without chapters: a single-file book divides into equal segments no longer than
    `MaxTrackDurationSeconds`.
  - Unsplittable: a chapter bigger than a card is reported and the book left out.
  - Apply with split points: valid points; a point that overflows a part.
  - Changing `YotoCardLimits` changes the plan and nothing else.
- **Playlist capacity and transfer.** An item with a chapter range counts and sends only those
  chapters, for chaptered single-file, multi-file and chapterless books.
- **Service tests (Infrastructure, in-memory DB).** Create, re-pack around locked cards, refresh
  with new books (including one whose sequence falls between locked cards), exclusions, delete
  leaves playlists.
- **Controller tests (Api).** Ownership (404 for another user's set), 409 for an existing set,
  422 for invalid breaks, 409 for an over-capacity send.
- **Job tests.** Cards sent in order; a failed card does not stop the rest.
- **Frontend.** `cardSetStore` and the series page panel (Vitest): toggling mode, an invalid
  break snapping back, locked cards disabled.
- **Manual.** Make a Dragon Masters set, send it, play the first and last book of each card. Make a
  Harry Potter 1–4 set and check Goblet of Fire's second card starts at the chapter after the split.

## 13. As built

Where the code differs from the sections above, the code is right and this list says why.

- **Planner input (§5.1).** `ICardSetPlanner.Plan` and `Apply` take each book's per-chapter tracks
  (what `ITrackPlanner.Plan(media, Chapters)` returns) and the playlist grouping, and apply the
  grouping themselves. Capacity therefore matches what a card's playlist sends, and the planner can
  split a book on its chapters without a second input. Card playlists use `Auto` grouping, so short
  books stay one track each and a long series fits the 100-track limit.
- **`CardSetPlan.SplitChapters`.** The plan carries, per split book, the chapter list its ranges
  index into: the book's chapters, or the one-track segments of a book without chapters. Each part's
  `PlaylistItem.TrackDurations` stores that full list, so a part of a chapterless book knows its
  segments; the transfer cuts segment *i* as `[total·i/n, total·(i+1)/n)`.
- **Standalone books.** `Plan` takes an optional set of book indices that must sit alone. Re-packing
  uses it for the rest of a split book whose first parts are on Yoto.
- **Breaks around split books.** `Apply` adds the breaks on each side of a book that needs splitting
  when the caller leaves them out, and splits a book without split points by the set's mode.
- **Locked cards.** A card that is `Queued` or `Transferring` is locked as well as `Transferred`. Every card up
  to the last locked one keeps its books (§5.5), so nothing slides under a sent card. Changing the
  mode or exclusions (§7.4 `PATCH`) re-reads the series and re-packs only the cards after it,
  continuing a partly sent split book from the chapter after its last sent part.
- **Responses (§7.3).** `GET /api/cardsets/{id}` returns `books` and `cards` in the same shape as the
  plan preview, each card adding `playlistId`, `status`, `locked`, `yotoCardId` and `errorMessage`,
  so the UI draws a preview and a saved set the same way. Set `status` is
  `Draft | Sending | NeedsAttention | Complete`. `GET /api/cardsets` takes `?seriesId=` so the series
  page finds its set. Chapter titles of a saved split book are "Chapter *n*"; the playlist items
  cache durations, not titles.
- **Changed cards (§7.4 `PUT /breaks`).** A card whose books and range do not change keeps its title
  and status; a changed card gets its default title back and returns to draft.
- **Chapters changed in Audiobookshelf (§9).** Sending a part whose book no longer has the chapter
  count it was planned with fails that card with a message asking to re-plan; the other cards still
  send.
- **Send (§7.5).** Sending marks the chosen cards with a new playlist status, `Queued`, before the
  job is enqueued, so a second Send cannot queue cards an earlier job has not reached, which would
  make two Yoto cards for one card. Asking to send a queued card again is a `409`. If the job cannot
  be enqueued the cards go back to draft. The job has no automatic retry; when Hangfire re-runs one
  after a restart it skips cards already on Yoto, a card that fails before sending starts is marked
  `Failed` rather than left queued, and cancelling it puts the cards it had not reached back to
  draft. Progress is pushed on the existing hub (below); the series page also re-reads the set
  every 5 s while any card is queued or being sent, so a missed push only delays an update.
- **Books that cannot be split (§5.4 step 4).** Creating a set, or re-packing one, is refused
  (`422`) while a book has a chapter bigger than a card; the series page marks the book and asks for
  it to be left out first. Saving would otherwise drop it from the set without a trace.
- **Cards are not plain playlists.** The `/api/playlists` endpoints that change a playlist refuse
  (`409`) one that belongs to a set; reading it still works.
- **UI (§8).** Breaks and split points move with buttons (one book or one chapter either way)
  instead of dragging. The chapter strip shows where each split falls.
- **New books (§7.3, §7.4 refresh).** `GET /api/cardsets/{id}` returns `newBooks` (id, title,
  sequence) rather than `newBookIds`, so the page can name them; `?newBooks=false` skips the
  Audiobookshelf read for the re-reads while sending, and every other response leaves it `null`.
  If Audiobookshelf can't be read the list is empty rather than the read failing.
  `POST /refresh` puts new books on the last card when that card is not on Yoto, queued or a part
  of a split book, and on new cards after it; every earlier card keeps its books. A new book whose
  place in the series is before those cards goes on the first card that can change, with a message.
  A new book that cannot be split is refused (`422`) like at create.
- **Series grid (§7.3 status, §8).** `GET /api/cardsets/status?seriesIds=a,b,c` returns, per series
  with a set, the card and sent counts, whether it is sending, the set status, and `knownBookIds`
  (books on its cards or left out) instead of `newBookCount`: the grid already has each series' book
  ids, so it counts new books itself and the endpoint makes no Audiobookshelf calls.
  `GET /api/cardsets/limits` gives the card limits so the grid can show "Fits on 1 card" from a
  series' total duration; that is a size estimate only, and the series page's plan is the answer.
  The filter chips (All / Not on cards / New books since last cards) filter the page of series shown.
- **Live progress (§7.5).** The hub has `JoinCardSetGroup` / `LeaveCardSetGroup` (owner only, group
  `cardset:{id}`). The send job pushes `CardSetProgress { cardSetId, playlistId, cardIndex, status,
  errorMessage }` when a card starts, lands or fails, and when a cancel returns cards to draft. The
  series page re-reads its set on each push. Pushes are best effort: a failed push never stops a send.
- **Transfers page (§7.3 sends, §8).** Closes [#148](https://github.com/O-Mutt/AudioYotoShelf/issues/148):
  card set sends never wrote a `CardTransfer`, so the Transfers page showed nothing while a set sent.
  It now lists sets from `GET /api/cardsets/sends` next to the book transfers. The send raises
  `TransferListChanged` through `ICardSetProgressNotifier.SendsChangedAsync` (best effort, like the
  card pushes).
