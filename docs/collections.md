# Collections — content sync + chronological ordering

A collection can auto-fill its contents from a **content sync provider** and
order them with a **sort provider**. The two are independent and stored
separately on `Collection` (`src/Vora.Domain/Entities/Library/Collection.cs`):

- **Content**: `ContentSyncProviderId` + `ContentSyncExternalId` (the *Franchise
  or universe* text).
- **Sort**: `SortProviderId` + `ExternalListId` (the *Describe the collection and
  ordering* text).

Providers implement `ICollectionSyncProvider` (content) and `IChronologyProvider`
(sort). Built-ins: AI (`openai_list`, `openai_chronology`), Trakt, MDbList, IMDb.

## AI List (`openai_list`)

Best for a **defined franchise or shared universe**, not open genres. A genre or
mood grouping ("all my kung-fu movies", "all my horror") can't be enumerated by
the model and it doesn't know your library — use a **Smart Playlist** instead
(filters your library by genre metadata; see [`playlists.md`](playlists.md)).

`FetchItemsAsync` runs one generation prompt, then up to `MaxCompletenessPasses`
(2) **critic passes** that name entries the previous list missed, merged with a
normalized dedup key; it stops when a pass adds nothing. Output is movies
(title + year) and per-season entries (show + season number).

## Matching the list to the library (`CollectionMembershipResolver`)

External ids (TMDB/IMDb) match first; everything else matches by title
(`TitleMatch.MatchKeys` in `CollectionMembershipEntry.cs`):

- Studio possessive prefix (`Marvel's Agents of S.H.I.E.L.D.` ↔ `Agents of…`).
- One-shot / "presents" designation prefixes (`Marvel One-Shot: Item 47`).
- Trailing-year strip (`Hawkeye (2021)` ↔ `Hawkeye`).
- Movies require an **exact year** when the list gives one, so `Daredevil` (2003)
  never matches `Daredevil` (2015).
- **Seasons: once any listed season resolves a show, every season of that show
  the library actually has is included** — the AI decides which shows belong, the
  library decides which seasons exist. This fixes partially-listed shows (the AI
  lists Peacemaker S1; the library has S2 → both are added).

## Sync reconciliation (`CollectionSyncService`)

- Caches the resolved membership (`ContentSyncCacheJson`, `ContentSyncedAt`).
- Subtracts `ExcludedMediaIdsJson` — an item the admin removed stays out.
- **Mirror mode** (`MirrorList`): drops items no longer in the list, but keeps
  `CollectionItem.ManuallyAdded` items and never wipes the collection on a total
  match failure.
- A sync that finds nothing (the AI returned no titles, or titles matched nothing
  in the library) raises a **persistent admin notification** — but only when the
  collection is currently empty, so scheduled re-syncs of healthy collections stay
  quiet.
- A membership change queues a chronology re-evaluate.

## Chronological ordering (`openai_chronology`)

Each item gets an in-universe **setYear** (decimal). `GetChronologicalOrderAsync`
runs these passes:

1. **Seed** from each item's cached `KnownSetYear` (locked ones are flagged).
2. **Batch scoring** — items chunked, each batch retried a few times.
3. `VerifyPlacementAsync` — a review pass over the scored years.
4. `AnchorContemporarySeasons` — pin a TV season near its aired era so a
   long-running show isn't dropped a decade off.
5. `RepairSeasonYears`.
6. `EnforceDistinctSetYears` — break ties so the order is deterministic.

Items rank by setYear (falling back to release year). **Locked items are never
changed by any pass.**

## Editing and locking a year

- `CollectionItem.InUniverseYear` / `InUniverseYearLocked` — an admin sets and
  locks a year in the **Manual Sort Order** modal
  (`Vora.Web/src/components/Collections/ReorderCollectionModal.tsx`). A locked
  year survives every re-sync. This is the deterministic fix when the AI's
  set-year is off — e.g. a present-day film that defaults to its release year, or
  the same title scored differently across two collections.
- `CollectionItem.ManuallyAdded` — an admin-added item that mirror mode won't
  drop; adding one clears any prior exclusion.

## What to type in each field

- **AI List — *Franchise or universe***: just the universe name, e.g.
  `Marvel Cinematic Universe`. The prompt already pulls movies **and** every
  season, so don't add "movies and shows", and never write "… Movies" alone — that
  narrows it to films and drops the shows. Name the specific universe when a brand
  has several (`DC Extended Universe`, not bare `DC Universe`, which sweeps the
  Arrowverse and animated lines too).
- **Chronology — *Describe the collection and ordering***: universe name plus the
  ordering intent, e.g. `Marvel Cinematic Universe in in-universe chronological
  order`. Left blank, it falls back to the collection's title.

## Tasks

Full sync, content sync, reorder, and reevaluate-order are queued through
`ITaskQueueManager` and serialized per collection by the `CollectionKey`
resource key, so a collection's own tasks never run concurrently or race. See
[`scanning-and-tasks.md`](scanning-and-tasks.md).
