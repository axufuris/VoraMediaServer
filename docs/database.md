# Database

PostgreSQL with the `vector` extension, accessed via EF Core.

## DbContext

- **`Vora.Infrastructure/Persistence/VoraDbContext.cs`** is the main context. It's split into partials and per-aggregate `Configure*` helpers (`ConfigureMedia`, `ConfigureCollections`, `ConfigureClientDevices`, `ConfigurePlaylists`, …). When adding entities, follow the same pattern: a new `Configure<Aggregate>` method called from `OnModelCreating`.
- `DbSet<T>` properties are grouped by aggregate at the top of the class.
- Be aware of **vector columns** when modifying entities or writing queries — they're used for embeddings on media items.

## Migrations

- Migrations live in `/src/Vora.Infrastructure/Migrations`.
- Create them from the Visual Studio **Package Manager Console**:
  - `add-migration FooNameOfMigration`
  - `update-database`
- **Never** edit a checked-in migration. If you need to fix it, add a new one.
- The history was **squashed to a single `Initial`** before the first public release. Two live third-party API keys had been property initializers on `ServerSetting`, and because `SeedSystemDefaults` uses `HasData`, they were baked into the `HasData` call of all 59 migration snapshots. Since a checked-in migration must not be hand-edited, regenerating from the model was the only way to remove them. A database created before that squash cannot be migrated forward — it has to be recreated.
- **Everything after `Initial` is one migration, `ChangesSinceInitial` (`20261004151213_ChangesSinceInitial`)**, collapsed on 2026-10-04 from the 2026-10-03 squash, `AddSetupGuide` and `AddSmartListSourcesAndChannelFavorites`. `MigrationHistoryTests` pins exactly these two ids.
  - It has a new id, so every existing database (QA included, whose history ends at the old squash id `20261003013801_StoreSongProfileListsAsArrays`) runs it once. History rows for migrations no longer in the assembly are ignored.
  - What older databases may already have is written to be a no-op there:
    - The song-profile, embedding-source and `AiPlaylistMatchWindow` columns use `ADD COLUMN IF NOT EXISTS`, with the 0.04 default the old chain left.
    - The Setup Guide columns, and the "an admin exists → `Skipped`" update, run only when `ServerSettings.SetupGuideStatus` is missing, so a database that ran `AddSetupGuide` keeps its guide progress.
    - The smart-list and channel-favorites changes (including the favorites move out of the prefs JSON) are new everywhere.
  - It keeps the settings row (no `GLOBAL_SETTINGS` delete), as `StopSeedingServerSettings` did. The model snapshot is byte-identical to the uncollapsed chain's.
  - The old data-only fixes (Specials season titles, popularity re-queues, the JSON → `text[]` conversion) are not in it; they had already run wherever there was data.
  - The Postgres tests build QA's real state with `LegacyDatabase` (`Initial` + the old columns + QA's history rows, optionally `AddSetupGuide` too) and migrate it forward.
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
| `ServerSetting.BackupConfigurationJson` | JSON-serialized `BackupSettings` (cadence, retention, included section keys, last-run timestamp). See `docs/backups.md` | `text` |
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
