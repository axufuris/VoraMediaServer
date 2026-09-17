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

`GenerateDescriptionAsync` (a default no-op on `ICollectionSyncProvider`, so
other providers are unaffected) returns a short blurb about the universe. The
sync fills the collection's `Description` with it **only when that field is
blank**, so a description an admin typed is never overwritten.

## Chronology: how a wrong year gets in, and out

Ordering is driven by a single decimal `setYear` per item — whole part the story
year, fraction sequencing within it — assigned by the model, then audited in one
pass that sees the whole ordered list.

The audit asks for each reviewed title's **absolute** story year, worked out on
its own merits before comparing with the current value. It used to ask whether an
item looked right *relative to its neighbours*, and let the model omit anything
it judged fine. Those two together let a whole class of error survive: a title
given its release year by mistake sits among the other titles of that release
year, so it reads as locally consistent, and silence confirms it. Black Panther
(set 2016, released 2018) landed 13 places late in a real collection exactly that
way. The audit now requires an explicit value for every reviewed index.

**"Sync timeline" is a full re-derivation, not a top-up.** The endpoint passes
`force: true`, which discards the cached `InUniverseYear` of every *unlocked*
item so the whole collection is scored and audited again. Locked items keep their
year and are excluded from the audit — locking is how you pin a placement you have
checked yourself. Scheduled/background runs pass `force: false` and skip entirely
while the item set is unchanged, so they never re-audit.

## Smart collections (rule-based)

A collection can instead be filled by a **rule** rather than a provider list.
It stores a `SmartPlaylistDefinition` in `Collection.RulesJson` plus a
`SmartMediaType`, and reuses the smart-playlist engine: `CollectionSyncService`
calls `ISmartPlaylistEvaluator.EvaluateIdsAsync` to get the matching
`MediaItem` ids, then feeds them through the same membership reconciliation as a
provider sync (mirror always on for smart, so membership tracks the rule, while
manual adds and exclusions still win). The rule builder is the shared
`RuleTreeEditor` component (content-only fields — no profile-scoped
watched/rating), surfaced as a **Smart (rule-based)** option in the collection
create/edit modals.

**Phase 1 is Movies only** — the evaluator's Movies path returns `Movie`
entities, which map 1:1 to collection membership; its Shows path returns
episodes and Music returns tracks, so those need a show/album-level evaluator
before they fit a collection (a fast-follow). Freshness: smart collections join
the scheduled content-sync sweep (gated by a stamped `ContentSyncedAt`) and
re-sync immediately on save; on-scan re-evaluation is a deferred enhancement.
Use a smart collection for a **genre / decade / rating** grouping and the AI
List for a **franchise** — the two are complementary.

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

## AI model, freshness, and accuracy

The AI providers share one model setting — `collections_model` on the
`openai_recommendations` plugin (`OpenAiRecommendationProvider`). It defaults to
**`gpt-5`** and also offers `gpt-5-mini`/`gpt-5-nano` and the older
`gpt-4o`/`gpt-4.1` families. The `gpt-5` and `o*` families are reasoning models
that reject a custom sampling temperature, so `OpenAiClient` omits the
`temperature` parameter for them and keeps sending it (`0.2`) for the `gpt-4.x`
models. Cost is a few thousand tokens per sync — pennies on any of these.

Accuracy measures already in place: the list prompt tells the model to **stay
within one continuity** (so an "Arrowverse" list doesn't sweep in the separate DC
Animated Movie Universe films), the completeness passes catch missing entries,
and matched shows expand to all their library seasons.

**The freshness limit — worth remembering.** Every LLM is frozen at a training
cutoff, so *no* OpenAI model reliably knows very recent content (e.g. James
Gunn's DCU / Superman 2025) — the collection comes back empty or thin and the
sync raises the "got no titles" admin alert. Switching to Claude or Gemini does
**not** fix this; it is a cutoff problem, not a vendor problem. Two ways to get
current/accurate results:

- **Curated providers, already built:** the Trakt, MDbList, and IMDb list +
  chronology providers pull human-maintained lists that, for popular franchises,
  are more accurate and current than any LLM — and cost nothing (IMDb needs no
  key). Prefer these when a good curated list exists.
- **A web-search-grounded provider — not built yet, a future option for more
  accuracy.** A model that searches the web at query time stays current.
  **Perplexity Sonar** is the cost-effective fit (per-token cheaper than gpt-5,
  plus a small ~\$0.005/request search fee → still pennies per sync); OpenAI's,
  Gemini's, or Claude's web-search tools would also work but add a larger
  per-search fee. Adding one means a new `ICollectionSyncProvider` /
  `IChronologyProvider` plus an API key. Revisit this if collections need live,
  up-to-the-minute coverage.

## Tasks

Full sync, content sync, reorder, and reevaluate-order are queued through
`ITaskQueueManager` and serialized per collection by the `CollectionKey`
resource key, so a collection's own tasks never run concurrently or race. See
[`scanning-and-tasks.md`](scanning-and-tasks.md).
