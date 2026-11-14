# Database

PostgreSQL with the `vector` extension, accessed via EF Core.

## DbContext

- **`Vora.Infrastructure/Persistence/VoraDbContext.cs`** is the main context. It's split into partials and per-aggregate `Configure*` helpers (`ConfigureMedia`, `ConfigureCollections`, `ConfigureClientDevices`, `ConfigurePlaylists`, …). When adding entities, follow the same pattern: a new `Configure<Aggregate>` method called from `OnModelCreating`.
- `DbSet<T>` properties are grouped by aggregate at the top of the class.
- Be aware of **vector columns** when modifying entities or writing queries — they're used for embeddings on media items.

## Queries that load more than one collection

No splitting behaviour is set globally. A query that loads two or more collection navigations, through `Include`/`ThenInclude` or through a projection, has to say `.AsSplitQuery()`. That includes a collection nested inside another, such as `UserVM.Projection`, which loads profiles and each profile's schedules. Without it, EF logs `MultipleCollectionIncludeWarning` and runs a single query that repeats the parent rows for every child combination.

- Combine `Take`/`Skip` with a split query only after an `OrderBy` on a unique key. Each split query reruns the filter, and without a stable order each one can pick different rows.
- `PostgresDatabase.NewContext()` turns the warning into an exception, so a database test that runs an unsplit query fails. `PostgresQuerySplittingTests` covers the queries that needed it: the user lists, playlist summaries, titles waiting for an embedding, and the duplicate-show merge.

## The detail page in two queries

A split query runs once per collection, so a projection with many lists costs many queries. `GET /api/media/{id}` (`MediaRepository.GetMediaDetailsAsync`) is two:

1. `MediaDetailsVM.Fields`: the item's own fields, and what it shows of its season's and show's, through `ApplyAccessFilters`. A title the viewer may not see stops here.
2. `MediaDetailsQuery`: one statement that returns every list as one JSON document. It returns:
   - the genres, studios, directors and cast, each from the first of the item, its season and its show that has any (`docs/frontend-client-ui.md`, Inherited facts)
   - the videos, extras, markers, and files with their tracks
   - a show's seasons, or a season's episodes
   - the profile's watch state and ratings
   - the show's episodes on disk, which come off its Upcoming list

   It is keyed by what the first query found, so it reads only what the item's type shows.

As a single projection holding every list, the page cost 26 split queries whatever the item was:
- a list for each fallback: an episode's own genres, its season's and its show's
- a show's seasons and a season's episodes even on a movie's page
- each of those joining the item, its season, its show and its library again

Then came 2 to 6 more for the watch state. On a copy of QA that took 35–56 ms; the two queries take 6–10 ms.

- `PostgresMediaDetailsTests` loads every page of eight random libraries both ways and checks they match. `MediaDetailsOracle` is the old projection and watch-state code, kept verbatim. The tests also pin the page at two queries.
- Audio and subtitle tracks come in the file's stream order, with embedded subtitles before sidecars. The old projection gave them no order, and on QA most files' audio tracks came back shuffled.

## Migrations

- Migrations live in `/src/Vora.Infrastructure/Migrations`.
- Create them from the Visual Studio **Package Manager Console**:
  - `add-migration FooNameOfMigration`
  - `update-database`
- **Never** edit a checked-in migration. If you need to fix it, add a new one.
- The history was **squashed to a single `Initial`** before the first public release. Two live third-party API keys had been property initializers on `ServerSetting`, and because `SeedSystemDefaults` uses `HasData`, they were baked into the `HasData` call of all 59 migration snapshots. Since a checked-in migration must not be hand-edited, regenerating from the model was the only way to remove them. A database created before that squash cannot be migrated forward — it has to be recreated.
- **Everything after `Initial` is one migration, `ChangesSinceInitial` (`20261114051129_ChangesSinceInitial`)**: the 2026-10-03 squash, `AddSetupGuide`, `AddSmartListSourcesAndChannelFavorites`, `PendingTasks` (`docs/task-resume.md`), the default row order, `MediaItems.FullyRefreshedAt`, `ServerSettings.HardwareAccelerationType` (`docs/hardware-acceleration.md`), `StreamSessions.IpAddress` / `Location` (`docs/streaming.md`) `MediaLibraries.LocalMetadataMode` / `PreferLocalArtwork` (`docs/local-metadata.md`), the `ProfileTitlePlaybackChoices` table and the profile's audio and subtitle preferences (`docs/track-selection.md`), `IX_Albums_ArtistId_Title` no longer being unique (`docs/music-library-scanning.md`), and the measured video size (`MediaVideoTracks.Width` / `Height` / `FrameRate`) with the output size of a stream (`StreamSessions.OutputWidth` / `OutputHeight`, `docs/streaming.md`), the profile's library sort (`UserProfiles.LibrarySort` / `LibrarySortDescending`, `docs/frontend-client-ui.md`), keeping HDR in a transcode (`ServerSettings.PreserveHdrInTranscodes`, `docs/streaming.md`), playing the next episode automatically (`UserProfiles.AutoPlayNext`, `docs/analysis-and-markers.md`), where Play on a show starts and how a season's episodes are laid out (`UserProfiles.UpNextOrder` / `EpisodeLayout`, `docs/frontend-client-ui.md`), whether a device draws image subtitles (`ClientDevices.RendersImageSubtitles`, `docs/subtitles.md`), clearing video bitrates higher than their whole file's, left by stale Matroska statistics tags (`MediaVideoTracks.Bitrate`, `docs/streaming.md`), the video format each Live TV channel's last timeshift session saw (`IptvChannels.VideoCodec` … `VideoFormatSeenAt`, `docs/iptv-and-dvr.md`), TMDB keywords with theme collections and the yearly collection window (`MediaItems.Keywords`, `Collections.VisibleEveryYear`, the `CollectionThemes` and `CollectionThemeVerdicts` tables, `docs/theme-collections.md`), what each song was played from (`TrackPlayHistory.Source` / `SourceId`, `docs/music-and-audio.md`), the home rows' sort indexes (`IX_MediaItems_PresentByReleaseDate` / `PresentByDateAdded`, see Indexes below), search's trigram indexes (`IX_MediaItems_TitleTrigram` and three more, see Indexes search relies on), the stored For You rows (the `RecommendationSnapshots` table, see The stored For You rows below), providers that need an account (`IptvPlaylists.SourceType` / `ServerUrl` / `Username` / `PasswordCiphertext` / `UserAgent` / `Referer` / `UseProviderGuide` and the four `Account*` columns, `IptvChannels.UserAgent` / `Referer`, `docs/iptv-and-dvr.md`), taking "AI: " off the stored AI rows' titles in favour of an `isAi` flag (`RecommendationSnapshots.Rows`, `docs/for-you.md`), and taking deleted libraries and IPTV playlists out of the access lists (`Users` / `UserProfiles` `AllowedLibraryIds` / `AllowedIptvPlaylistIds`, see Column constraints to remember below). It is re-issued under a new id each time something is folded in: `20261004151213` (first), `20261004183407` (adds `PendingTasks`), `20261004192607` (Recent Recordings moves up to follow Favorite Channels; the one 1.0 shipped with), `20261009020545` (adds `FullyRefreshedAt`; the one 1.1 shipped with), `20261011233931` (adds `HardwareAccelerationType`), `20261012232953` (adds the stream's address and location), `20261013214138` (adds the two local-file library settings), `20261014181817` (adds remembered playback choices), `20261015010727` (adds the four `UserProfiles` track preference columns), `20261015235423` (makes the album title index non-unique), `20261017000904` (adds the video and output sizes), `20261017011545` (adds the profile's library sort), `20261017194415` (adds keeping HDR in a transcode), `20261017231930` (adds autoplay), `20261018072825` (adds the up-next order and the episode layout), `20261018192130` (adds whether a device draws image subtitles), `20261020021843` (clears impossible video bitrates; the one 1.2 shipped with), `20261022014009` (adds the channel video format), `20261029013035` (adds keywords, the yearly window and the theme tables), `20261110034736` (adds the play source), `20261111053514` (adds the home rows' sort indexes), `20261111183206` (adds search's trigram indexes), `20261112020121` (adds the stored For You rows), `20261112032459` (adds IPTV provider accounts), `20261113230423` (takes "AI: " off the stored AI For You rows), `20261114051129` (takes deleted libraries and playlists out of the access lists). `MigrationHistoryTests` pins `Initial` and the current id.
  - **Folding something in means:** add it to the `Up` SQL guarded so it does nothing where it already exists (`ADD COLUMN IF NOT EXISTS`, `CREATE TABLE IF NOT EXISTS`, a `DO` block checking for it), add its drop to `Down`, delete the old `ChangesSinceInitial` files, and save the new pair under a new id with the current model in its designer (generate a throwaway migration for the model, then rename it). Add the old id to `LegacyDatabase` with a Postgres test that migrates a database on it forward.
  - Because the id is new, every existing database runs it once. That includes QA and every 1.0 and 1.1 install, whose history ends at an earlier `ChangesSinceInitial` id, and any database still on the old squash id `20261003013801_StoreSongProfileListsAsArrays`. History rows for migrations no longer in the assembly are ignored.
  - What older databases may already have is written to be a no-op there:
    - The song-profile, embedding-source and `AiPlaylistMatchWindow` columns use `ADD COLUMN IF NOT EXISTS`, with the 0.04 default the old chain left.
    - The Setup Guide columns, and the "an admin exists → `Skipped`" update, run only when `ServerSettings.SetupGuideStatus` is missing, so a database that ran `AddSetupGuide` keeps its guide progress.
    - The smart-list and channel-favorites section (columns, table, default rows, row order, the favorites move out of the prefs JSON) is one `DO` block guarded on `to_regclass('"ProfileChannelFavorites"') IS NULL`, so a database that already ran it skips it whole. None of its data steps run twice, and a change to the default row order reaches new servers only; existing ones keep the order they have.
    - `PendingTasks` is `CREATE TABLE IF NOT EXISTS`, so a database that already has it keeps its saved tasks.
    - `FullyRefreshedAt` and `HardwareAccelerationType` (`integer NOT NULL DEFAULT 0`, which is Auto) are `ADD COLUMN IF NOT EXISTS`.
    - `StreamSessions.IpAddress` (`character varying(45)`) and `Location` (`character varying(128)`) are `ADD COLUMN IF NOT EXISTS`, both nullable, so existing sessions keep every value they have.
    - `MediaLibraries.LocalMetadataMode` (`integer NOT NULL DEFAULT 0`, which is Off) and `PreferLocalArtwork` (`boolean NOT NULL DEFAULT FALSE`) are `ADD COLUMN IF NOT EXISTS`, so every existing library starts with both off.
    - `UserProfiles.PreferredAudioLanguage`, `PreferredSubtitleLanguage` (both `character varying(16)`), `SubtitleMode` (`integer`, nullable) and `PreferHearingImpairedSubtitles` (`boolean NOT NULL DEFAULT FALSE`) are `ADD COLUMN IF NOT EXISTS`, so every existing profile starts on Automatic.
    - `UserProfiles.LibrarySort` (`integer`, nullable) and `LibrarySortDescending` (`boolean NOT NULL DEFAULT FALSE`) are `ADD COLUMN IF NOT EXISTS`, so every existing profile starts on the server default.
    - `ServerSettings.PreserveHdrInTranscodes` (`boolean NOT NULL DEFAULT TRUE`) is `ADD COLUMN IF NOT EXISTS`, so an existing server starts keeping HDR wherever its GPU can.
    - `UserProfiles.AutoPlayNext` (`boolean NOT NULL DEFAULT TRUE`) is `ADD COLUMN IF NOT EXISTS`, so every existing profile starts playing the next episode by itself.
    - `UserProfiles.UpNextOrder` and `EpisodeLayout` (both `integer NOT NULL DEFAULT 0`: in order, and the row) are `ADD COLUMN IF NOT EXISTS`. A data step sets every resume point under 30 seconds (`WatchStateTransition.MinimumResumeSeconds`) to 0, since nothing writes one any more, so no Resume at 0:12 or Continue Watching tile is left behind. Running it twice changes nothing.
    - `ClientDevices.RendersImageSubtitles` (`boolean`, nullable) is `ADD COLUMN IF NOT EXISTS`, so every existing device keeps burning image subtitles in until its app says it draws them.
    - The seven `IptvChannels` video format columns are `ADD COLUMN IF NOT EXISTS`, all nullable, so every channel starts unknown and is learned on its next timeshift session.
    - `MediaItems.Keywords` (`text[]`, nullable: never asked) and `Collections.VisibleEveryYear` (`boolean NOT NULL DEFAULT FALSE`) are `ADD COLUMN IF NOT EXISTS`, and `CollectionThemes` / `CollectionThemeVerdicts` are `CREATE TABLE IF NOT EXISTS`, so every title waits for its keywords (the next scan fetches them) and every collection keeps its dated window.
    - `TrackPlayHistory.Source` (`integer NOT NULL DEFAULT 0`, which is None) and `SourceId` (`uuid`, nullable) are `ADD COLUMN IF NOT EXISTS`, so every play already recorded keeps its row and has no source.
    - `RecommendationSnapshots` is `CREATE TABLE IF NOT EXISTS`, and starts empty: each profile's For You rows are built on its next visit (`docs/for-you.md`).
    - A data step rewrites the stored rows of the AI provider (`ProviderId = 'openai_recommendations'`): each row object gains `"isAi": true` and loses a leading `"AI: "` from its `title`, keeping its order and every other field. It only touches a snapshot with a row not already flagged, so running it again (every fold-in re-runs it) changes nothing, and a title the model itself began with "AI: " after the change is kept. Other providers' rows, and a `Rows` value that isn't an array, are left as they are.
    - A data step takes every id that names no `MediaLibraries` row out of `Users.AllowedLibraryIds` and `UserProfiles.AllowedLibraryIds`, and every id that names no `IptvPlaylists` row out of both `AllowedIptvPlaylistIds`. Deleting a library or a playlist used to leave its id behind. The other ids keep their order and the list is written the way EF writes it (`["…","…"]`); a list with nothing to take out, and a value that isn't an array, are not touched, so running it again changes nothing. `HasAllLibraryAccess` / `HasAllIptvAccess` are left alone: a restricted profile whose only library is gone keeps an empty list, which is no access.
    - The IPTV provider columns are `ADD COLUMN IF NOT EXISTS`: `SourceType` `integer NOT NULL DEFAULT 0` (M3U), `UseProviderGuide` `boolean NOT NULL DEFAULT TRUE`, the rest nullable. Every existing playlist stays the M3U playlist it was, with no login and no headers, and every channel keeps its address.
    - The two sort indexes are `CREATE INDEX IF NOT EXISTS`. Building them holds off writes to `MediaItems` while it runs (reads go on), well under a second for QA's 50,000 titles (about 40 ms on a desktop).
    - The five size columns are `ADD COLUMN IF NOT EXISTS`, all nullable. A track without a size is probed when it is next played (`docs/streaming.md`), so no data step fills them.
    - `IX_Albums_ArtistId_Title` is dropped if it exists and created again as a plain index, so it runs the same on every database. An artist can have two albums of one title in different folders (Weezer's self-titled albums); the unique index made the scan's insert of the second one fail.
  - It keeps the settings row (no `GLOBAL_SETTINGS` delete), as `StopSeedingServerSettings` did.
  - The old data-only fixes (Specials season titles, popularity re-queues, the JSON → `text[]` conversion) are not in it; they had already run wherever there was data.
  - The Postgres tests rebuild each earlier state with `LegacyDatabase` and migrate it forward:
    - the old chain: `Initial` + the old columns + its history rows, optionally `AddSetupGuide` too;
    - the earlier combined migrations: the full schema with the old default row order under the old id, without `PendingTasks` for the first. Row order, default rows, favorites and saved tasks come through unchanged;
    - the 1.0 migration (`ReleasedMigration`): the full schema without `FullyRefreshedAt` and `HardwareAccelerationType` under `20261004192607`. It gains both columns and keeps its saved tasks, settings and row order;
    - the 1.1 migration (`SecondReleaseMigration`): the full schema without `HardwareAccelerationType` under `20261009020545`. It gains the column on Auto and keeps its device setting, saved tasks, settings and row order.
    - the hardware acceleration migration (`HardwareAccelerationMigration`): the full schema without the two `StreamSessions` columns under `20261011233931`. It gains them empty and keeps its stream sessions and its hardware acceleration choice.
    - the stream-origin migration (`StreamOriginMigration`): the full schema without the two `MediaLibraries` columns under `20261012232953`. It gains them switched off and keeps its libraries.
    - the local-metadata migration (`LocalMetadataMigration`): the full schema without `ProfileTitlePlaybackChoices` under `20261013214138`. It gains the empty table and keeps its libraries' local-metadata settings.
    - the remembered-choices migration (`RememberedChoicesMigration`): the full schema without the four profile track preference columns under `20261014181817`. Its profiles gain them on Automatic and keep their other settings.
    - the track-preferences migration (`TrackPreferencesMigration`): the full schema with the unique album title index under `20261015010727`. Its albums are kept, and an artist can then have a second album of the same title.
    - the album-index migration (`AlbumIndexMigration`): the full schema without the five size columns under `20261015235423`. Its video tracks and stream sessions are kept and gain the columns empty.
    - the video-dimensions migration (`VideoDimensionsMigration`): the full schema without the two library sort columns under `20261017000904`. Its profiles keep their settings and start on the server default.
    - the library-sort migration (`LibrarySortMigration`): the full schema without `PreserveHdrInTranscodes` under `20261017011545`. Its settings and profiles are kept, and the setting starts on.
    - the HDR-transcode migration (`HdrTranscodeMigration`): the full schema without `UserProfiles.AutoPlayNext` under `20261017194415`. Its profiles keep their settings and start with autoplay on.
    - the autoplay migration (`AutoPlayMigration`): the full schema without `UserProfiles.UpNextOrder` / `EpisodeLayout` under `20261017231930`. Its profiles keep their settings and start in order on the row; a resume point under 30 seconds is cleared and a longer one kept.
    - the up-next migration (`UpNextOrderMigration`): the full schema without `ClientDevices.RendersImageSubtitles` under `20261018072825`. Its devices keep their capabilities and gain the column empty.
    - the image-subtitles migration (`ImageSubtitlesMigration`): the full schema without the channel video format columns under `20261018192130`. Its video bitrates above their whole file's are cleared.
    - the 1.2 migration (`ThirdReleaseMigration`): the full schema without the channel video format columns under `20261020021843`. Its channels are kept and gain the columns empty.
    - the channel-video-format migration (`ChannelVideoFormatMigration`): the full schema without the keyword column, the yearly flag and the theme tables under `20261022014009`. Its collections keep their dated windows and its titles their rows; they gain the columns (no keywords, not yearly) and the empty tables.
    - the theme-collections migration (`ThemeCollectionsMigration`): the full schema without the two `TrackPlayHistory` source columns under `20261029013035`. Its plays are kept and gain no source.
    - the play-source migration (`PlaySourceMigration`): the full schema without the two sort indexes under `20261110034736`. It gains them and keeps its titles.
    - the home-row index migration (`HomeRowIndexMigration`): the full schema without the four search indexes under `20261111053514`. It gains them and keeps its titles.
    - the search-index migration (`SearchIndexMigration`): the full schema without `RecommendationSnapshots` under `20261111183206`. It gains the table empty, with its foreign key to `UserProfiles`, and keeps its profiles and watch history.
    - the AI row flag migration (`AiRowFlagMigration`): the full schema under `20261113230423`, with access lists that name two deleted libraries (the ids left on QA) and a deleted playlist. They lose those ids and keep the rest in order, a restricted profile whose only library and playlist were deleted keeps empty lists and no access, lists with nothing deleted are unchanged, and migrating again changes nothing.
    - the IPTV providers migration (`IptvProvidersMigration`): the full schema under `20261112032459`, with stored For You rows. Its AI rows lose "AI: " and gain `isAi`; the local rows and AI rows already flagged are unchanged, and migrating again changes nothing.
    - the For You rows migration (`ForYouRowsMigration`): the full schema without the IPTV provider columns under `20261112020121`. Its playlists stay M3U playlists without credentials, keep their tuner limits and channels, and gain the columns; an Xtream account can then be saved.
  - A test that adds rows to an earlier state through EF writes every column the current model has. Seed through `LegacyDatabase.SeedAsync`, which adds back the columns folded in since and drops them after, or seed before the state drops them (the `seed` callback of the `Build…StateAsync` methods). Extend it when a fold-in adds a column to a table those tests seed (the IPTV provider columns are in it).
- **`ServerSetting` is not seeded.** The single row (`Id = "GLOBAL_SETTINGS"`) is created from the entity's property initializers by `SystemSettingsRepository.GetSettingsForUpdateAsync`, called once at startup (`EnsureServerSettingsAsync`) right after migrations. The initializers are the only defaults, so changing one needs **no migration**. It used to be `HasData`-seeded, which copied every default into the snapshot (and disagreed with the initializers on nightly scan and detections); `StopSeedingServerSettings` removed the seed from the model and was edited to keep the existing row instead of deleting it.
- **Never give a credential a default value in an entity.** A property initializer on a seeded entity ends up in every migration snapshot from then on, and no later edit removes it from the ones already written. Keys belong in plugin settings (`Required = true`, `Type = "password"`, no default), which is where every provider already reads them from.

## Indexes search relies on

Search matches each word with `ILIKE '%word%'` (`SearchRepository`, `MusicRepository.SearchAsync`). An ordinary index can't serve a match in the middle of a string, so every search read every title, person, artist and album: about 60 ms on QA's data (50,000 titles and songs, 77,000 people), mostly people and songs at about 21 ms each.

- **The indexes:** `ChangesSinceInitial` installs Postgres's `pg_trgm` extension and builds trigram GIN indexes that `ILIKE` uses as it is:
  - `IX_MediaItems_TitleTrigram` (movies, shows and songs)
  - `IX_Actors_NameTrigram`
  - `IX_Artists_NameTrigram`
  - `IX_Albums_TitleTrigram`

  On QA's data a search went from about 60 ms to 9–15 ms. A very common word like "the" went from 59 to 29 ms, since it matches thousands of rows. Building them took 0.4 s, and they take about 8 MB.
- **Optional by design.** `pg_trgm` ships with Postgres (both `pgvector/pgvector` images have it) and is a trusted extension: a user with CREATE on the database may install it, no superuser needed. Vora's own `vector` extension already asks for more than that.
- **The guard:** the extension and the indexes are created inside a `DO` block that turns any error into a warning. A server without `pg_trgm`, or a database user who may not install it, still migrates and starts; search then reads every row as it did before. The query is the same either way.
- **Keep them out of the model.** Never move this out of its block, and never declare the extension with `HasPostgresExtension`: EF would then emit a bare `CREATE EXTENSION` that can stop the server starting. `Postgres/PostgresSearchIndexTests` migrates a database as a user who may not install extensions, and checks it migrates and search still works.
- **Adding them later:** an install that skipped them can add them by running the block from the migration once, as a user who may install extensions.

## Indexes the home screen relies on

Continue Watching and the six default library rows are on every Home, on every client, so they are kept to one cheap statement each. On QA's data (50,000 titles, one profile with 13,000 watch states) Continue Watching was 1,203 queries and about 3 s before.

- **Continue Watching is one SQL statement**, `ContinueWatchingQuery` (`UserMediaStateRepository.GetContinueWatchingAsync`, through `SqlQueryRaw`). It applies the Play button's rules (`EpisodeToPlay`, `docs/frontend-client-ui.md`) to every show at once, newest show first, and stops when the row is full: about 40 ms on a desktop and 115 ms on QA for that profile. The comments in the class say which shapes keep Postgres from re-scanning per show. Raw SQL can't run on the in-memory provider, so its tests are `Postgres/PostgresContinueWatchingTests`, including a check against the one-pick-per-show version it replaced (`ContinueWatchingOracle`) over random watch histories.
- **The rows' sorts read from two expression indexes**, partial on `"MissingSince" IS NULL`: `IX_MediaItems_PresentByReleaseDate` on `(("ReleaseDate" IS NOT NULL), "ReleaseDate")` for the release-date sorts, and `IX_MediaItems_PresentByDateAdded` on `(COALESCE("LastContentAddedAt", "AddedAt"))` for Date Added. With them Postgres reads the first 20 titles in order instead of sorting the whole table for every row: the three rows that mix types or list episodes went from about 35 to 11 ms on a desktop. EF can't model an expression index, so they exist only in the `ChangesSinceInitial` SQL, and only match while `SmartListRepository.ApplySortOrder` produces exactly that `ORDER BY`. `Postgres/PostgresHomeRowIndexTests` fails if a default row stops reading from its index.
- **No covering index on episodes or seasons.** One (`("Id") INCLUDE ("SeasonId", "EpisodeNumber")` on present episodes) took Continue Watching from about 38 to 28 ms on a desktop, but it made Postgres work out the show tiles' watched flag (`TitleWatchState.IsPlayed`) for every episode in the library at once, and Recently Added Shows went from 12 to 30 ms. Measure the show rows before adding one.
- **JIT is off on Vora's connections** (`VoraConnectionString.WithJitOff`, which adds `-c jit=off` to the connection string's `Options` unless it sets `jit` itself). A stock Postgres compiles any query its planner prices above `jit_above_cost` to machine code before running it, and Vora's big projections cross that line: on QA a library page's query (every title with its watched state and rating) took 5.3 s, nearly all of it compiling, and 0.15 s without. Per connection, so it holds on any user's Postgres without touching its settings.
- Otherwise QA's Postgres runs on stock settings (`shared_buffers` 128 MB, `random_page_cost` 4). The tables these read are about 200 MB with a 99.9% buffer hit rate, and `random_page_cost` 1.1 changed neither plan, so neither is set.
- **Measuring a page:** the 2026-11 audit counted the SQL of each client screen's requests against a copy of QA. Besides the above it found For You loading each row's tiles with its own query (now one, `docs/for-you.md`) and the mood tiles asking per mood (now one read, `docs/ai-playlists.md`). For You also built its local rows on every request, 65 statements; it now reads stored rows in three (The stored For You rows, below). A detail page (`GET /api/media/{id}`) ran 26 to 28 split queries, one per list it shows; it is two since #258.

## The stored For You rows

For You reads the rows each recommendation provider last built instead of asking the provider while the page waits (`docs/for-you.md`). They live in `RecommendationSnapshots`, one row per profile, page and provider:

- **Key:** `(ProfileId, LibraryScope, ProviderId)`, the primary key, which is also what For You reads by. `LibraryScope` is the library's id on a library's For You tab and `Guid.Empty` (`RecommendationSnapshot.AllLibraries`) on Home. It is not nullable on purpose: a unique key over a nullable column needs Postgres 15's `NULLS NOT DISTINCT` to treat two Home rows as the same, and self-hosted servers run older Postgres. So there is no foreign key to `MediaLibraries`; a deleted library's rows are never read again.
- **`Rows`** is `jsonb`: the provider's `RecommendationListDto` list (title, description, weight, `isAi`, the title ids, any TMDB ids) as camelCase JSON, held as a string in the model and read and written by `RecommendationSnapshotRepository`. A value that doesn't read back as a list counts as no rows, and is built again. Ten rows of fifteen titles take about 1 KB, compressed.
- **`GeneratedAt`** is when the build started, so a play recorded during a build leaves the rows out of date rather than missed.
- **The profile's rows go with it**: `ProfileId` has a cascading foreign key to `UserProfiles`. Clearing a profile's viewing history deletes them too (`UserMediaStateRepository.ClearViewingHistoryAsync`).
- **Writes are one upsert** (`INSERT … ON CONFLICT … DO UPDATE`), so a page's first build and the worker's rebuild can't trip over the key. An older build never replaces a newer one, and nothing is written for a profile deleted meanwhile.
- **The read** is one statement through the primary key. For the local rows it also carries the profile's latest `UserMediaStates.LastPlayedAt` and `UserMediaRatings.RatedAt`, as two uncorrelated subqueries that run once (InitPlans) through the existing `(ProfileId, MediaItemId)` indexes: about 3 ms on a desktop for a profile with 13,000 watch states scattered through the table, against 0.02 ms for the AI rows' read alone. An index on `(ProfileId, LastPlayedAt)` made it 0.1 ms but was left out: every progress report writes `LastPlayedAt`, and would then update that index too.
- **Not backed up** (`BackupCoverageTests` lists it as left out): it is rebuilt on the next visit.

## Column constraints to remember

These are easy to overflow accidentally — note the limit when sending data from the frontend or external services:

| Entity | Property | Max length |
| --- | --- | --- |
| `ClientDevice.DeviceId` | unique device UUID | 128 |
| `ClientDevice.ClientName` | sent via `X-Vora-Client` header | 64 |
| `ClientDevice.DeviceName` | sent via `X-Vora-Device` header | 128 |
| `ClientDevice.DeviceType` | sent via `X-Vora-Device-Type` header | 32 |
| `ClientDevice.OperatingSystem` | sent via `X-Vora-OS` header (use a parsed short name, not raw `navigator.userAgent`) | 64 |
| `ClientDevice.LastIpAddress` | IPv4/IPv6 | 45 |
| `ClientDevice.Location` | reverse-geo string | 128 |
| `Media.Title` / `SortTitle` / `OriginalTitle` | | 500 |
| `Media.OriginalLanguage` | ISO code | 8 |
| `Media.Edition` | denormalized display value, synced from the best (highest-resolution) `MediaPart.Edition` (see `docs/scanning-and-tasks.md`) | 64 |
| `MediaPart.Edition` | per-file edition (Director's Cut, IMAX, …), parsed from the filename; the source of truth for editions | 64 |
| `Media.Status` | | 32 |
| `Media.HomePage` | URL | 1024 |
| `Media.ContentRating` | | 32 |
| `Media.TmdbId` / `ImdbId` / `TvdbId` | external IDs | 64 |
| `ServerSetting.AdminThemeId` | active admin theme id; falls back to `"vora-default"` if the persisted id no longer resolves in `IThemeRegistry` | (default 64) |
| `ServerSetting.SmtpHost` / `SmtpUsername` / `SmtpFromAddress` | SMTP config (see `docs/email.md`) | 256 |
| `ServerSetting.SmtpFromDisplayName` | display name in From header | 128 |
| `ServerSetting.SmtpPasswordCiphertext` | DataProtection-encrypted SMTP password | `text` |
| `ServerSetting.EmailPublicBaseUrl` | base URL for absolute links in emails | 512 |
| `ServerSetting.BackupConfigurationJson` | JSON-serialized `BackupSettings` (cadence, retention, excluded section keys, last-run timestamp; a legacy included-keys list is converted on read). See `docs/backups.md` | `text` |
| `ServerSetting.EnableTrashAutoPurge` | when true, items soft-deleted longer than `MissingMediaRetentionDays` are permanently purged by the nightly maintenance task. See `docs/scanning-and-tasks.md` | (bool) |
| `ServerSetting.MissingMediaRetentionDays` | days a soft-deleted (trashed) item is kept before auto-purge is eligible | (int) |
| `ServerSetting.ResolveMovieTvdbIds` | when true, the nightly metadata pass resolves missing `TvdbId`s for movies **and** shows; admins can also trigger a one-time pass. See `docs/scanning-and-tasks.md` | (bool) |
| `MediaItem.MissingSince` | UTC timestamp set when all of an item's files disappear from disk (soft-delete / Trash); `null` while the item is present. Restored files clear it | (timestamp) |
| `PreservedUserMediaData.ContentKey` | stable content identity (`ContentIdentity.Compute`) used to restore ratings + watch-state when a purged item is later re-added. See `docs/auth-and-devices.md` | 256 |
| `Track.Moods` / `Themes` / `GoodFor` | native Postgres `text[]` (no JSON converter), so `t.Moods.Contains(mood)` translates and mood browsing stays in LINQ with the music access filter. The only array columns; other string lists are JSON text. See `docs/ai-playlists.md` | `text[]` |
| `MediaLibrary.ExcludeFilters` | string collection; a file whose name contains any entry (case-insensitive) is skipped by the scanner and folder watcher (e.g. `.TDARR`, transcoder temp dirs). See `docs/scanning-and-tasks.md` | (list) |
| `UserProfile.ShowtimesLocation` | per-profile ZIP/city used by the SerpApi theater plugin; null falls back to admin default | 120 |
| `User` / `UserProfile` `.AllowedLibraryIds` / `AllowedIptvPlaylistIds` | JSON text of ids (`converters.GuidList`), not rows, so no delete cascades to them. `AccessLists` takes an id out in the same transaction as the delete: `LibraryRepository.DeleteLibraryAsync`, `IptvRepository.DeletePlaylistAsync`, and the IPTV playlist restore for each playlist it removes. A new way to delete a library or playlist must call it too. With `HasAllLibraryAccess` / `HasAllIptvAccess` off, an empty list is no access, never all. `Postgres/PostgresAccessListTests` | `text` |
| `RequestServer.ProvidesReleaseCalendar` | when true, the Radarr/Sonarr calendar plugins read this server's URL+API key via `IRequestServerLookup` (see `docs/plugins.md`). Allows a single Arr instance to power both requests and the release calendar | (bool) |
| `EmailTemplate.Key` | string-converted `EmailTemplateKey` enum, primary key | 64 |
| `EmailTemplate.SubjectOverride` | admin-edited subject override | 256 |
| `EmailTemplate.HtmlBodyOverride` / `TextBodyOverride` | admin-edited body overrides | `text` |
| `EmailDeliveryLog.TemplateKey` / `Status` | string-converted enums | 64 / 16 |
| `EmailDeliveryLog.ToAddress` / `Subject` | recipient + rendered subject | 256 |
| `EmailDeliveryLog.ErrorMessage` | failure detail (truncated to 512 in the VM) | 2048 |
| `PasswordResetTicket.TokenHash` | SHA-256 of the reset token (hex, lowercased) | 128 |
| `InvitationTicket.Email` | invited email address | 256 |
| `InvitationTicket.TokenHash` | SHA-256 of the invite token (hex, lowercased) | 128 |
| `RegistrationTicket.SecretCode` | legacy 3-word shared invite code | 128 |

When auditing column lengths, look at `ConfigureXxx` helpers in `VoraDbContext.cs` — those have the truth.

## DateTimes crossing the HTTP boundary

Npgsql refuses to write a `DateTime` with `Kind=Unspecified` to a `timestamp with time zone` column — only UTC is supported. Every `DateTime` column in Vora is `timestamptz`, so a query parameter that reaches EF Core with the wrong `Kind` throws `ArgumentException` from inside the query rather than returning a bad result.

Minimal API binding decides the `Kind`, and it depends entirely on what the client sent:

| Query value | Bound `Kind` |
| --- | --- |
| `2026-09-06T00:00:00.000Z` | `Utc` |
| `2026-09-06T00:00:00-05:00` | `Utc` |
| `2026-09-06T00:00:00` | `Unspecified` |
| `2026-09-06` | `Unspecified` |

So a client sending an ISO instant works and a client sending a bare date takes the whole endpoint down. The calendar failed exactly this way: the web client sends `Date.toISOString()` and was fine, while a client sending a zone-less date made every request log `Cannot write DateTime with Kind=Unspecified` and return an empty calendar.

**Any endpoint taking a `DateTime` from the query string must normalize it** with `AsUtc()` (`Vora.Api/Extensions/QueryDateExtensions.cs`) before passing it on. `Unspecified` is taken as UTC; `Local` is converted, not relabelled. Note the consequence for a zone-less value: it is read as UTC rather than as the viewer's local midnight, which can shift a range by the client's offset. The alternative is guessing the client's timezone, which is worse — clients should send an instant.

## Test data / seeds

The DbContext includes API key seeds for testing IPTV / metadata providers. Don't remove them when refactoring; the user keeps them in for testing and removes them manually before release.
