# Spec: card set sources

**Status:** Proposed ([#149](https://github.com/O-Mutt/AudioYotoShelf/issues/149)).
**Scope:** build a card set from an Audiobookshelf collection or from books picked in
AudioYotoShelf, not only from an Audiobookshelf series. The cards, the planner and sending stay as
[series-card-sets.md](series-card-sets.md) describes.

## 1. Goals and non-goals

**Goals**

1. Make a card set from an Audiobookshelf collection, in the collection's order.
2. Make a card set from books picked in the Library, in an order the user sets.
3. Keep every card set behaviour that isn't about where the books come from:
   - the planner (Fill and Balance), card breaks and split points;
   - locked sent cards;
   - sending, live progress and the Transfers page (#148).
4. Keep a collection set in step with its collection. A refresh places books added to the
   collection. It takes books removed from the collection off unsent cards, and follows the
   collection's new order on unsent cards.
5. List every card set, of every kind, in one place.

**Non-goals**

- Editing an Audiobookshelf collection from AudioYotoShelf. Collections are curated in
  Audiobookshelf.
- A set whose books span libraries. A set belongs to one library, like an Audiobookshelf
  collection.
- Changing a sent card. Sent cards stay locked whatever happens to the source.

## 2. Current state

- `CardSet.AbsSeriesId` is required. `CardSetService` reads books only through
  `LoadSeriesOrderAsync` (by series sequence, then title) and `FetchBooksAsync` (each book's media
  and chapters).
- `CardSetPlanRequest` and `CreateCardSetRequest` take `AbsSeriesId` and `AbsLibraryId`. Responses
  carry `AbsSeriesId`.
- `GET /api/cardsets/status?seriesIds=` feeds the badges on the Library series grid.
- A set lives on its series page (`/series/:seriesId`, `SeriesDetailView` and `CardPlanPanel`).
- `PlaylistsController` sends a list of any books to one card. Nothing in the client calls it, and
  it returns 409 when the books don't fit.

## 3. Design summary

A set gets a **source**: `Series`, `Collection` or `Custom`. The source answers one question:
*which books, in what order?* Everything after that is already source-neutral and stays as it is:
`FetchBooksAsync`, the planner, `Specs`, `RepackUnsentAsync`, locking and sending.

| Source | Identified by | Order | Refresh |
| --- | --- | --- | --- |
| Series | `SourceId` = Audiobookshelf series id | sequence, then title (as today) | new books are placed; removed books are ignored (as today) |
| Collection | `SourceId` = Audiobookshelf collection id | the collection's order | new books are placed; removed books leave unsent cards; unsent cards follow the new order |
| Custom | `CustomItemIds` on the set | the stored list | none: the user changes the list directly (§6.4) |

## 4. Audiobookshelf collections

What follows was read from the Audiobookshelf server source (`CollectionController.js`,
`LibraryController.js`, `models/Collection.js`). The published API docs describe themselves as out
of date.

- **List:** `GET /api/libraries/{libraryId}/collections`.
  - Returns `{ results, total, limit, page, … }`.
  - `limit=0` means no limit. Paging happens in memory on the server.
  - `minified` does not shrink `books`: each entry is a full expanded library item.
- **One:** `GET /api/collections/{id}`.
  - Returns `{ id, libraryId, name, description, books, lastUpdate, createdAt }`.
  - `books` holds full library items, in the user's order (`collectionBook.order`).
- **Scope:** a collection has a `libraryId` and no owner. Every user who can open the library can
  read it.
- **Hidden books:** books the user can't see (tag or explicit-content limits) are left out of
  `books`. If that leaves a non-empty collection with nothing visible, the server answers 404.
- **Changes:** `lastUpdate` is not bumped when books are added or removed. Change detection must
  compare book ids, not `lastUpdate`.

New `IAudiobookshelfService` methods: `GetCollectionsAsync(libraryId)` and
`GetCollectionAsync(collectionId)`. The second returns the ordered book ids and names; book media
still comes from `FetchBooksAsync`, so a collection set reads books the same way a series set does.
A 404 from `GetCollectionAsync` means the collection is gone or hidden (§8).

## 5. Data model

**Migration `AddCardSetSources`**

- Add `CardSets.SourceKind` (int, not null, default `0` = `Series`).
- Rename `CardSets.AbsSeriesId` to `SourceId` and make it nullable. It stays required for `Series`
  and `Collection`, and is null for `Custom`. Existing rows keep their series id and become
  `Series` sets with no data change.
- Add `CardSets.CustomItemIds` (`text[]`, not null, default `{}`), the ordered books of a `Custom`
  set. It is empty for the other kinds.
- `ExcludedItemIds` stays. It applies to `Series` and `Collection`. A `Custom` set leaves a book out
  by removing it from `CustomItemIds`.

```csharp
public enum CardSetSourceKind { Series, Collection, Custom }

public class CardSet : BaseEntity
{
    public CardSetSourceKind SourceKind { get; set; } = CardSetSourceKind.Series;
    public string? SourceId { get; set; }            // series or collection id; null for Custom
    public required string AbsLibraryId { get; set; }
    public List<string> CustomItemIds { get; set; } = [];
    // …unchanged
}
```

**One set per source.** `ExistingSetIdAsync` keys on `(SourceKind, SourceId)` for `Series` and
`Collection`, so a second set for the same series or collection is still refused. A user can have
any number of `Custom` sets.

## 6. Service and API

### 6.1 Source abstraction

Replace `LoadSeriesOrderAsync` with a source reader that returns the set's name and its ordered
book refs:

```csharp
private sealed record SourceOrder(string Name, IReadOnlyList<SourceBookRef> Books);
private sealed record SourceBookRef(string Id, string Title, float? Sequence);
```

- **Series:** as today.
- **Collection:** the collection's books in collection order, with `Sequence = null`. A book's
  number within the set is its position, so default card titles read "Bedtime · Books 1–4", not the
  book's number in some other series.
- **Custom:** `CustomItemIds` in order, with `Sequence = null`. Titles come from each book's media,
  which `FetchBooksAsync` reads anyway.

`PlanAsync`, `CreateAsync`, `RefreshAsync` and `NewBooksAsync` call the reader instead of the series
methods. `FetchBooksAsync` takes the ref list it already takes, and its log line names the set
rather than the series.

### 6.2 Requests and responses

- `CardSetPlanRequest` and `CreateCardSetRequest` replace `AbsSeriesId` with:
  - `SourceKind`, which defaults to `Series`;
  - `SourceId`, the series or collection id;
  - `ItemIds`, the ordered books of a `Custom` set, required for `Custom` and ignored otherwise.
- `AbsLibraryId` stays required.
- Validation answers 400 when:
  - `SourceId` is missing for `Series` or `Collection`;
  - `ItemIds` is empty for `Custom`;
  - a `Custom` book isn't in `AbsLibraryId` (checked when its media is fetched).
- `CardSetResponse` and `CardSetSummaryResponse` replace `AbsSeriesId` with `SourceKind` and
  `SourceId`, and add `CustomItemIds` to the full response.
- The client ships with the server, so this is a breaking change to the API with no alias period.
- `GET /api/cardsets/status` takes `collectionIds` as well as `seriesIds`. Each result carries
  `SourceKind` and `SourceId` in place of `AbsSeriesId`.

### 6.3 Collections

- `GET /api/libraries/{libraryId}/collections`: the user's visible collections in the library. Each
  entry has the id, name, book count and the first book's id for a cover. It proxies Audiobookshelf
  with `limit=0`.
- `GET /api/libraries/collections/{collectionId}`: the collection with its books, like
  `GET /api/libraries/series/{seriesId}` for a series page.

### 6.4 Custom sets

- `PUT /api/cardsets/{id}/books` with `{ "itemIds": [...] }`:
  - replaces `CustomItemIds`;
  - then repacks the unsent cards through `RepackUnsentAsync`, as `UpdateAsync` does for breaks and
    exclusions.
- The rules for sent cards:
  - A book on a locked card can't be removed. The request answers 409 and names the card.
  - Reordering can't move a book across a locked card. Locked cards keep their books, and the order
    applies to the unsent tail only.
  - Adding a book places it in the unsent tail.
- `400` for a `Series` or `Collection` set: their books come from Audiobookshelf.

### 6.5 Refresh for collections

`RefreshAsync` for a `Collection` set:

1. Reads the collection.
2. **Removed books:** books on unsent cards that are no longer in the collection leave those cards.
   A removed book on a locked card stays on it, and the response says so ("X was removed from the
   collection but is already on card 2").
3. **New books:** books in the collection that the set doesn't know (`KnownBookIds`) are placed as a
   series refresh places them.
4. **Order:** the unsent tail is ordered by the collection's current order. Locked cards don't move,
   so a book now earlier than a locked card gets the "comes earlier … went on card N" message that
   series sets already give.

`NewBooksAsync` for a `Collection` set also reports removed books, so the set page can offer a
refresh when either list is non-empty.

## 7. UI

- **Library: Collections tab.** A grid of the library's collections next to Series, with the same
  card-set badges (`GET /status?collectionIds=`).
- **Collection page.** `/collection/:collectionId?libraryId=` shows the collection's books and the
  existing `CardPlanPanel`, as the series page does. "Refresh" also appears when books were removed.
- **Custom sets.**
  - Library multi-select (`useSelection` already exists) gets "New card set…". It asks for a name,
    then opens the set page with the plan.
  - The set page (`/cardsets/:cardSetId`) shows `CardPlanPanel` and an ordered book list. The list
    supports drag to reorder, remove, and "Add books", which goes back to the Library in
    add-to-set mode.
  - Locked books can't be dragged or removed.
- **Card sets page.** `/cardsets` lists every set: name, kind, "N of M cards sent", status. Each row
  links to the set's own page:
  - series → `/series/:id`;
  - collection → `/collection/:id`;
  - custom → `/cardsets/:id`.
  - A nav entry sits next to Transfers and Cards.
- **Transfers page (#148).** A card set row links to the same per-kind page instead of always
  `/series/:id`.
- **Book page.** "Add to card set…" lists the user's `Custom` sets in the book's library.

## 8. Edge cases

- **Collection deleted or hidden** (Audiobookshelf 404):
  - The set keeps its cards, and sent cards stay on Yoto.
  - The set page says the collection can't be read.
  - Refresh is disabled, and editing or sending unsent cards still works.
  - `NewBooksAsync` returns empty and logs a warning, as it does for a series today.
- **Collection emptied:** a refresh removes every unsent card, and the locked cards remain. A set
  with no cards left can be deleted from its page.
- **Book deleted from Audiobookshelf:** for all kinds, fetching it fails as it does for a series
  book today. A `Custom` set shows the book as missing and lets the user remove it.
- **Same book in two sets:** allowed, as two series sets can share a book today.
- **Series converted to a collection:** not supported. They are separate sets, and the user deletes
  the old one.

## 9. Phases

1. **Collections.**
   - Migration, the source abstraction and the API changes in §6.1–6.3 and §6.5.
   - Collections tab, collection page and status badges.
   - The card sets page, so collection sets have a list.
   - #148's links.
2. **Custom sets.**
   - §6.4, the multi-select "New card set…", the custom set page and "Add to card set…" on the book
     page.
   - Retire `PlaylistsController`'s standalone endpoints (§10).

## 10. Open questions

1. **`PlaylistsController`.** Once custom sets exist, the standalone endpoints (`POST /api/playlists`,
   `/items`, `/series`, `/order`, `/transfer`) duplicate a one-card custom set. The proposal is to
   remove them in phase 2 and keep `Playlist` as the card entity. Nothing in the client uses them.
2. **Custom set library.** Taken from the first picked book. If a later "Add to card set…" names a
   book from another library, it answers 400. The alternative, cross-library sets, conflicts with
   the one-library rule for collections, so the proposal is to refuse.

## 11. Test plan

- **Migration:**
  - existing sets become `Series` with `SourceId` set to their old series id;
  - `CustomItemIds` is empty.
- **Source reader:**
  - series order is unchanged (the existing tests pass as they are);
  - a collection follows Audiobookshelf's order with `Sequence` null;
  - a custom set follows `CustomItemIds`.
- **Collection refresh:**
  - added books are placed;
  - removed books leave unsent cards;
  - a removed book on a locked card stays, with a message;
  - a reorder applies to the tail only;
  - a 404 keeps the set and disables refresh.
- **Custom sets:**
  - create, reorder, add and remove;
  - 409 for removing a locked book;
  - 400 for a book from another library and for `PUT /books` on a non-custom set.
- **API:**
  - ownership checks on the new endpoints;
  - validation for each `SourceKind`;
  - `status` with `collectionIds`.
- **Client (vitest):**
  - the Collections grid and badges;
  - the set page's reorder and lock rules;
  - the card sets page links for each kind.
- **E2E:** none. The Playwright suite covers only auth and admin today, and card sets have no E2E.
  Adding one is a separate piece of work.
