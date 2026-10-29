# Theme collections and seasonal visibility

A collection for an occasion — Halloween, Valentine's Day, Mother's Day, Christmas — fills itself from the library with the **AI Theme** content provider (`openai_theme`, `OpenAiThemeProvider`), and shows only around the occasion with a **seasonal window that repeats every year**. Franchises stay with AI List (`docs/collections.md`); a genre or mood is a Smart collection.

## How a sync fills it

The admin types the theme in **Theme or occasion** (`ContentSyncExternalId`, e.g. `Halloween`). Each sync (`CollectionSyncService`, on the collection's interval or Sync now):

1. **Profile, once per theme.** One `collections_model` request returns a 3–4 sentence description of a title that belongs (what it is about, when it is set, its imagery, written like a plot summary, about the theme and not just its mood) and up to 8 TMDB keyword names that mark such a title (`halloween`, never `horror`). The profile is embedded with `embedding_model`. Both are kept in `CollectionThemes` (`Theme`, `Profile`, `Keywords` `text[]`, `Embedding` `real[]`, `EmbeddingModel`), one row per collection.
2. **Tagged titles go in.** Movies and shows in the collection's library (all libraries for a global one) whose `Keywords` overlap the profile's (`&&` in SQL), not in Media Trash. No AI.
3. **The closest titles are checked.** The 150 titles nearest the profile's vector (`MediaItemEmbeddings` of the current embedding model only, an unstamped row counting as `text-embedding-3-small`; movies and shows; not trashed), ordered by `<=>` on the indexed column (`EF.Property<Pgvector.Vector>(…)`, so no `!` and the HNSW index still serves it). Those without an answer yet go to the model 60 at a time with title, year, kind, genres and the first 240 characters of the overview; it returns the indexes that belong. A title belongs when it is about the occasion or set during it, not when it only shares a genre or mood.
4. **Answers are kept.** Every answer, yes or no, is saved in `CollectionThemeVerdicts` (collection + title), so a later sync asks only about titles that are new among the closest 150. A collection whose theme is settled costs nothing: no profile, no judging.
5. **Members** are the tagged titles plus every yes, less the collection's exclusions (`ExcludedMediaIdsJson`; removing a title from the collection adds it there). Mirror mode drops titles no longer found and keeps titles added by hand, as for any provider. A blank description is filled from the theme (`GenerateDescriptionAsync`).

When it asks again:

| Change | Profile | Answers |
|---|---|---|
| Theme text edited | new profile, keywords and vector | forgotten |
| Embedding model changed | kept, embedded again | kept |
| New titles near the theme | kept | asked about the new ones only |

With no embedding model (a local server without one), only tagged titles go in. With AI not set up, or no profile in the answer, the collection is left as it is. A theme sync that finds nothing for an empty collection raises an admin notice, and stamps `ContentSyncedAt` either way so the schedule waits its interval.

**First sync cost:** one profile request, one embedding, three judging requests for 150 candidates. Usage is logged under the plugin id, shown as **Theme Collections** on the AI usage page.

### The library-aware provider contract

AI List and the Trakt/MDbList/IMDb providers return outside titles that `CollectionMembershipResolver` matches by id or title. A theme provider finds titles **in the library**, so it also implements `ILibraryCollectionSyncProvider.FindInLibraryAsync(LibraryCollectionSyncRequest)` and returns media item ids; `CollectionSyncService` takes that path whenever the provider has it (its `FetchItemsAsync` returns nothing and is never called). It stays an `ICollectionSyncProvider` so it is discovered and listed like the others. The membership cache (`ContentSyncCacheJson`) is not used: a new title is picked up on the next sync, once the nightly task has embedded it or TMDB has tagged it. `CollectionSyncService` reads the collection through the named projection `SyncTarget` so tests can stub it.

## TMDB keywords

`MediaItem.Keywords` (`text[]`): `null` means never asked, empty means TMDB has none. A metadata refresh stores them for movies (`keywords.keywords`, already appended to the movie request for the stinger flags) and shows (`keywords` appended to the TV request, read from `keywords.results`), lowercased and once each (`TmdbMetadataProvider.KeywordNames`). A refresh whose provider gave none keeps the ones the title has.

Titles scanned before this have none, so a movie or TV library scan runs **Fetching keywords…** while any movie or show with a TMDB id still has `Keywords == null` (`ITitleKeywordService` → `IMetadataProvider.FetchKeywordsAsync`, `movie/{id}/keywords` or `tv/{id}/keywords`). It asks once per title: 100 at a time, four requests in parallel, a 404 saved as none, a failed request left for the next scan, and the pass stops when a whole page gets nothing back (TMDB down or no key). No queue method, so no `TaskRecipe`.

## Seasonal visibility

`Collection.VisibleStartDate` / `VisibleEndDate` with `VisibleEveryYear`:

- **Dated** (`VisibleEveryYear = false`): shown from the start date through the end date, both days included; a missing side is open.
- **Every year**: only the month and day count, every year; a window can run over New Year (December 20 through January 6); a missing side runs from January 1 or to December 31. The web stores the days in 2000, a leap year, so February 29 is allowed (`components/Collections/seasonalWindow.ts`).

One rule, `CollectionSeason.IsOpen`, decides it, against **today in the server's time zone** (`IServerCalendar` → `ScheduleClock.Today`, `ScheduleTimeZone`), not UTC. `CollectionSeason.InSeason` is the same rule as a query filter for search; `CollectionSeasonTests` checks the two agree on every day of three years, a leap day included.

Out of season a collection is hidden from viewers everywhere collections are listed: a library's Collections tab and the global tab (`CollectionHiddenReason.OutOfSeason`; admins see it under **Show hidden**, badged "out of season"; the Add to collection picker always lists it), and collection search. Its page still opens from a link. A Home row built from the collection shows only while **both** the collection's window and the row's own yearly window (`SmartList.ActiveStart/End…`) are open; the row's own window used to be ignored whenever it pointed at a collection, and the collection's dates only ever hid that row, although the editor said they hid the collection.

The editor (`SeasonalVisibilityFields`, in the create and edit modals) switches between two date fields and two month + day pickers (`MonthDayPicker`, shared with the Home row editor) with **Repeat every year**.

## Storage and backups

`CollectionThemes` and `CollectionThemeVerdicts` cascade with their collection (and a verdict with its title) and are left out of backups: the next sync makes them again. The collection itself, its titles, exclusions and window are in the `library.collections` section. The columns and tables were folded into `ChangesSinceInitial` (`docs/database.md`).
