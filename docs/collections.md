# Collections — content sync + chronological ordering

A collection can auto-fill its contents from a **content sync provider** and
order them with a **sort provider**. The two are independent and stored
separately on `Collection` (`src/Vora.Domain/Entities/Library/Collection.cs`):

- **Content**: `ContentSyncProviderId` + `ContentSyncExternalId` (the *Franchise
  or universe* text).
- **Sort**: `SortProviderId` + `ExternalListId` (the *Describe the collection and
  ordering* text).

A collection's poster can also be one of the posters that ship with Vora; see
[`vora-posters.md`](vora-posters.md).

Providers implement `ICollectionSyncProvider` (content) and `IChronologyProvider`
(sort). Built-ins: AI (`openai_list`, `openai_theme`, `openai_chronology`), Trakt, MDbList, IMDb. AI Theme fills a holiday or occasion collection from the library, and a collection can show only in its season, every year: [`theme-collections.md`](theme-collections.md).

## Order of collection lists

Every list of collections (the Collections tabs, the library's Collections tab, the global list and collection search) is ordered by **Sort Title**, falling back to the title when Sort Title is blank, then by title. A Sort Title such as `1_Halloween` therefore pins a collection to the top. The order of items inside a collection is the collection's own sort (Chronological, Alphabetical by the items' sort titles, release date, date added).

## Automatic (TMDB) collections: visibility and descriptions

- **Visibility** is one rule, `CollectionVisibility.HiddenReason`, driven by the library's `MinimumCollectionSize`: `0` hides every automatic (`SystemGenerated`) collection, `1`–`25` hides automatic ones with fewer items; admin-created collections always show; any empty collection is hidden. Default is 3 for new libraries (a request that omits it gets 3; an update that omits it keeps the current value). Applied to `GET /collections/library/{id}` and collection search. Admins pass `includeHidden=true` (ignored for everyone else) and get `hiddenReason` (`Hidden` / `Empty` / `OutOfSeason` / `AutomaticCollectionsHidden` / `BelowMinimumSize`) — the Collections tabs' **Show hidden (N)** toggle and the Add-to-collection picker use it. Deleting an automatic collection stays blocked: a rescan would recreate it.
- **Hidden by an admin.** Any collection, a system one included, can be hidden with the **Hide from viewers** switch in the edit modal (`Collection.Hidden`, sent as `hidden` on `PUT /collections/{id}`; a request without it leaves the setting alone), or straight from a collection tile: a right-click on the web, a long press on the other clients. A tile hides or shows it with `PUT /collections/{id}/hidden` `{ "hidden": true }` (admins only, 204, 404 for a collection that is gone), which changes that one setting and queues no sync, unlike a full update. It is then kept from viewers everywhere collections are listed: its library's Collections tab (or the Global tab and every library, for a global one), collection search and its Home row, whatever its season or size. Admins see it under **Show hidden**, badged "Hidden" (`CollectionHiddenReason.Hidden`, which takes precedence over every other reason). Its page still opens from a link. This is how to keep an automatic collection out of sight, since it can't be deleted. The Global tab has the same **Show hidden** toggle as a library tab.
- **Item counts in a library.** A global collection listed in a library tab shows every item it holds (`ItemCount = Items.Count`), which is what opening it shows: the John Wick collection with four films and The Continental reads 5 items on the Movies tab, not 4. Whether it is listed in that library at all, and the minimum-size rule, still go by the items in that library (`CollectionSummaryVM.LibraryItemCount`, not sent to clients), so a global collection with nothing in a library stays out of it.
- **Descriptions** come from TMDB's `collection/{id}` (the movie's `belongs_to_collection` has none). `Description == null` means never asked; `""` means TMDB had none. A movie library scan runs **Fetching collection descriptions…** only while some collection is still `null` (`ICollectionDescriptionService`), so it asks once per collection, never nightly; a failed request stays `null` and is retried on a later scan. A locked `Description` is never touched. Admins can ask again with **Fetch from TMDB** in the edit modal (`POST /collections/{id}/description/refresh`). Saving the modal with a blank description returns it to `null` (fetch again) unless TMDB already said it has none.

## AI List (`openai_list`)

Best for a **defined franchise or shared universe**, not open genres. A genre or
mood grouping ("all my kung-fu movies", "all my horror") can't be enumerated by
the model and it doesn't know your library — use a **Smart Playlist** instead
(filters your library by genre metadata; see [`playlists.md`](playlists.md)). A holiday or occasion is the **AI Theme** provider.

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

**One timeline, larger is later.** Both prompts say every `setYear` sits on one
number line, and a franchise that counts years from an in-universe event goes on
it with the years *before* that event negative (Star Wars: 32 BBY → `-32`,
0 BBY → `0`, 9 ABY → `9`). Without that rule the model wrote BBY counts as
positive numbers, so the prequel era sorted backwards: Rogue One (0 BBY) opened a
real collection and The Phantom Menace (32 BBY) closed it.

**Each item carries its synopsis.** The prompt line is `index: Title (Year)
[Type] — synopsis: …` (the title's overview, a season falling back to its
show's, whitespace collapsed and cut at 240 characters). A synopsis often names
the period outright; from its title alone the model put The Continental (aired
2023, set in 1975) at 2023, after John Wick: Chapter 4. A show title's
disambiguating year (`The Continental (2023)`) is dropped from the line, since
it is another release year and the aired year already follows.

Collections ordered before these changes keep their stored years until
**Sync timeline** re-derives them.

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
calls `ISmartPlaylistEvaluator.EvaluateCollectionIdsAsync` to get the matching
`MediaItem` ids, then feeds them through the same membership reconciliation as a
provider sync (mirror always on for smart, so membership tracks the rule, while
manual adds and exclusions still win). The rule builder is the shared
`RuleTreeEditor` component (content-only fields — no profile-scoped
watched/rating), surfaced as a **Smart (rule-based)** option in the collection
create/edit modals.

**What the rules match** (`SmartMediaType`) — a collection holds whole titles,
never episodes or tracks:

| `SmartMediaType` | Members |
|---|---|
| `Movies` | films |
| `Shows` | the `TvShow` itself, matched on the show's own first-air year, genres, content rating, ratings and date added |
| `Mixed` | both ("Movies & shows") |

A **global** collection picks one in the **Match** dropdown (new ones start on
Movies & shows). A **library** collection gets no choice: it matches its
library's kind (Movie and Home Video libraries → `Movies`, TV libraries →
`Shows`). Shows offer every movie field except Duration, which a show doesn't
have; switching a global collection to Shows drops any Duration rule. A smart
collection saved without a type (before this existed) still matches films.
Music has no collections — use a smart playlist. This is collection-only:
smart *playlists* keep matching episodes for Shows.

**Templates.** Above the rules, both modals have a **Template** list (*Start from a template…*) with one template for every Vora poster: 75 of them, in the posters' five groups (`components/Collections/smartCollectionTemplates.ts`, listed by `SmartCollectionRules`). Picking one replaces the rules, after asking when there are rules already; the limit and sort are left alone, and the list goes back to its prompt so the same template can be picked again. A template is only a starting point: edit the rules after.

| Group | What a template matches |
|---|---|
| Genres (25) | Genre contains the name (`Any`). Where television names a genre differently the rule covers it: Science Fiction also matches *Sci-Fi* (so *Sci-Fi & Fantasy*), Action matches *Action & Adventure*, War matches *War & Politics*. Kids also takes the ratings TV-Y, TV-Y7 and G; Superhero also looks for a few well-known heroes in the title; Anime needs a provider that tags the genre Anime. |
| Decades (11) | Release year between the first year of the decade and the ninth. |
| Ratings (11) | Content rating is exactly that rating (G, PG, PG-13, R, NC-17, TV-Y, TV-Y7, TV-G, TV-PG, TV-14, TV-MA). |
| Holidays (20) | Any of a few words in the title; Halloween, Memorial Day and Veterans Day also take the Horror or War genre. A holiday has no date or tag in a library's metadata, so these find few titles, and the template says so and points to the AI Theme provider (`theme-collections.md`), which fills a holiday from the library properly. Short words that sit inside others (*Mom* in *Moment*, *Tree* in *Street*) are left out on purpose. |
| Popular (8) | Built from the audience rating and the date added: Recently Added (added in the last 30 days), New Releases (last year and this year, **filled in from today** and not moving by themselves), Popular (rated above 7), Top Rated (above 8). Vora has no view counts, awards, box-office figures or vote counts, so Trending (rated above 7 and added in the last 90 days), Award Winners (rated above 8 in Drama, Biography, History or War), Hidden Gems (rated 7 to 8) and Blockbusters (rated above 6 in Action, Adventure, Science Fiction, Fantasy or Animation) stand in with the nearest thing the rules can say, and each one's note says so. The cut-offs assume ratings out of 10, as TMDB and IMDb give. |

Freshness: smart collections join
the scheduled content-sync sweep (gated by a stamped `ContentSyncedAt`) and
re-sync immediately on save; on-scan re-evaluation is a deferred enhancement.
Use a smart collection for a **genre / decade / rating** grouping and the AI
List for a **franchise** — the two are complementary.

## Cloning a collection

An admin clones a collection from its page: the **Clone Collection** button beside Edit. It is all in the web client, with no server endpoint of its own.

1. **`CloneCollectionDialog`** asks what kind of collection the copy will be: **Movie**, **Show** or **Global**. It starts on the kind the original is. Movie and Show mean a library of that kind (`smartMediaForLibrary`: Movie and Home Video libraries are Movie, TV libraries are Show; music has no collections); a kind with no library is disabled and says so, and when there is more than one library of the kind a **Library** list appears.
2. **`CreateCollectionModal`** then opens, headed **Clone Collection**, with `template` set to the original. It is the ordinary create dialog: the same fields, the same **Create Collection** button, and it creates through `POST /collections` and opens the new collection.

`cloneSeed` (`components/Collections/collectionSeed.ts`) fills the create dialog's fields from the original: title, description, sort title, seasonal window, default sort, the chronology provider with its list and auto-sync, the Auto-Fill provider with its list id, mirror and recheck interval, a smart collection's rules (with their limit and sort), the poster and backdrop, and which fields are locked. The title is copied as it is; it is the admin's to change. The poster counts as chosen, so a title never swaps it for a suggested Vora poster, and a collection without one is cloned without one. A smart collection's rules lose the fields the new kind has no use for (Duration on a show library or a shows-only global one), as when the Match is switched by hand; a global copy keeps what the original matched, or Movies & shows when it never said.

**Not copied.** The items a collection holds: an Auto-Fill or smart collection fills itself again by sync, and one managed by hand starts empty. Also not copied: Hide from viewers (the create dialog has no such field) and the automatic (TMDB) flag, since a clone is always an admin-created collection. An uploaded poster is shared with the original by address (`/api/artwork/custom/…`), not copied, so deleting that picture from the original's artwork list removes it from the clone as well.

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
`openai_recommendations` plugin (`OpenAiRecommendationProvider`), set under
**AI Settings**. On OpenAI its form default is **`gpt-5`**, with
`gpt-5-mini`/`gpt-5-nano` and the older `gpt-4o`/`gpt-4.1` families as
alternatives; any model the server lists can be chosen, and an empty value uses
the recommendations model. The connection itself can be another server
(`docs/ai-connection.md`), but this is the feature that suffers most on a small
local model: it leans on knowing films and shows, so expect invented titles and
wrong orders. The `gpt-5` and `o*` families are reasoning models
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
