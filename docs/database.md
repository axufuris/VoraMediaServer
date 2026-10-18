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

## Migrations

- Migrations live in `/src/Vora.Infrastructure/Migrations`.
- Create them from the Visual Studio **Package Manager Console**:
  - `add-migration FooNameOfMigration`
  - `update-database`
- **Never** edit a checked-in migration. If you need to fix it, add a new one.
- The history was **squashed to a single `Initial`** before the first public release. Two live third-party API keys had been property initializers on `ServerSetting`, and because `SeedSystemDefaults` uses `HasData`, they were baked into the `HasData` call of all 59 migration snapshots. Since a checked-in migration must not be hand-edited, regenerating from the model was the only way to remove them. A database created before that squash cannot be migrated forward — it has to be recreated.
- **Everything after `Initial` is one migration, `ChangesSinceInitial` (`20261018072825_ChangesSinceInitial`)**: the 2026-10-03 squash, `AddSetupGuide`, `AddSmartListSourcesAndChannelFavorites`, `PendingTasks` (`docs/task-resume.md`), the default row order, `MediaItems.FullyRefreshedAt`, `ServerSettings.HardwareAccelerationType` (`docs/hardware-acceleration.md`), `StreamSessions.IpAddress` / `Location` (`docs/streaming.md`) `MediaLibraries.LocalMetadataMode` / `PreferLocalArtwork` (`docs/local-metadata.md`), the `ProfileTitlePlaybackChoices` table and the profile's audio and subtitle preferences (`docs/track-selection.md`), `IX_Albums_ArtistId_Title` no longer being unique (`docs/music-library-scanning.md`), and the measured video size (`MediaVideoTracks.Width` / `Height` / `FrameRate`) with the output size of a stream (`StreamSessions.OutputWidth` / `OutputHeight`, `docs/streaming.md`), the profile's library sort (`UserProfiles.LibrarySort` / `LibrarySortDescending`, `docs/frontend-client-ui.md`), keeping HDR in a transcode (`ServerSettings.PreserveHdrInTranscodes`, `docs/streaming.md`), playing the next episode automatically (`UserProfiles.AutoPlayNext`, `docs/analysis-and-markers.md`), and where Play on a show starts and how a season's episodes are laid out (`UserProfiles.UpNextOrder` / `EpisodeLayout`, `docs/frontend-client-ui.md`). It is re-issued under a new id each time something is folded in: `20261004151213` (first), `20261004183407` (adds `PendingTasks`), `20261004192607` (Recent Recordings moves up to follow Favorite Channels; the one 1.0 shipped with), `20261009020545` (adds `FullyRefreshedAt`; the one 1.1 shipped with), `20261011233931` (adds `HardwareAccelerationType`), `20261012232953` (adds the stream's address and location), `20261013214138` (adds the two local-file library settings), `20261014181817` (adds remembered playback choices), `20261015010727` (adds the four `UserProfiles` track preference columns), `20261015235423` (makes the album title index non-unique), `20261017000904` (adds the video and output sizes), `20261017011545` (adds the profile's library sort), `20261017194415` (adds keeping HDR in a transcode), `20261017231930` (adds autoplay), `20261018072825` (adds the up-next order and the episode layout). `MigrationHistoryTests` pins `Initial` and the current id.
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
    - the migration before this one (`AutoPlayMigration`): the full schema without `UserProfiles.UpNextOrder` / `EpisodeLayout` under `20261017231930`. Its profiles keep their settings and start in order on the row; a resume point under 30 seconds is cleared and a longer one kept.
  - A test that adds rows to an earlier state through EF writes every column the current model has. Seed through `LegacyDatabase.SeedAsync`, which adds back the columns folded in since and drops them after, or seed before the state drops them (the `seed` callback of the `Build…StateAsync` methods). Extend it when a fold-in adds a column to a table those tests seed.
- **`ServerSetting` is not seeded.** The single row (`Id = "GLOBAL_SETTINGS"`) is created from the entity's property initializers by `SystemSettingsRepository.GetSettingsForUpdateAsync`, called once at startup (`EnsureServerSettingsAsync`) right after migrations. The initializers are the only defaults, so changing one needs **no migration**. It used to be `HasData`-seeded, which copied every default into the snapshot (and disagreed with the initializers on nightly scan and detections); `StopSeedingServerSettings` removed the seed from the model and was edited to keep the existing row instead of deleting it.
- **Never give a credential a default value in an entity.** A property initializer on a seeded entity ends up in every migration snapshot from then on, and no later edit removes it from the ones already written. Keys belong in plugin settings (`Required = true`, `Type = "password"`, no default), which is where every provider already reads them from.

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
