# Backup & restore

Vora writes configuration and user-data snapshots as zip files on disk, restores them selectively, runs them on a schedule, and estimates how big a backup will be before you take one. Every slice of state is a pluggable section, so adding one is a one-file change plus a registration.

## Sections

Every slice of state implements `IBackupSection` (`Vora.Application/Backups/IBackupSection.cs`):

```csharp
public interface IBackupSection
{
    string Key { get; }                       // "settings.server"
    string DisplayName { get; }               // "Server Settings"
    BackupSectionGroup Group { get; }         // Settings | Templates | Library | Iptv | Discovery | Security | UserData | Podcasts
    bool RequiresExplicitConfirm { get; }     // user data: unticked by default in the restore drawer
    bool CanGrowLarge { get; }                // drives the "large" chip in the Settings picker
    string? DestructiveWarning { get; }
    Task WriteAsync(IBackupWriter writer, CancellationToken ct);
    Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct);
}
```

Implementations live in `Vora.Infrastructure/Backups/Sections/`. `EntityTableBackupSection<TEntity>` covers "dump one DbSet": override `PrepareRowsAsync` to remap or skip rows on restore, `WriteReferencesAsync` to record identities, `ReplaceScope` to restore only part of a table, and `ReleaseReferencesAsync` to clear `NO ACTION` foreign keys before rows are removed.

| Key | Restores |
| --- | --- |
| `settings.server`, `settings.plugins`, `settings.webhooks` | `ServerSettings`, `PluginSettings`, `WebhookConfigs` |
| `library.definitions` | `MediaLibraries`: name, type, folders and every per-library setting. A library with the same name and type is updated in place (its id stays); one that isn't here is added under its old id when that id is free. Libraries not in the backup are left alone, and nothing is scanned — after the restore `BackupManager` restarts the folder watchers (`IFolderWatcherService.RestartAllWatchersAsync`) and the result tells the admin which libraries to scan |
| `settings.data-protection` | DataProtection key XML files (filesystem, not DB) |
| `templates.client-schedules`, `templates.email`, `templates.overlay` | template schedules, email overrides, overlay templates |
| `users.profiles` | `Users`, `UserProfiles`, `ProfileAccessSchedules` |
| `users.devices` | `ClientDevices`, `ProfileDeviceSettings` |
| `library.media-edits` | hand-made changes on rows a scan rebuilds — see **Metadata edits** below |
| `library.collections` | admin-made `Collections` (`SystemGenerated = false`), their `CollectionItems` and the images uploaded for them (`CollectionArtwork` with `IsUserUploaded`; the image files are not in the zip) |
| `library.smart-lists` | `SmartLists` (home rows) |
| `library.dedupe-rules` | `MediaDedupeSettings`, `MediaDedupeIgnoredGroups` |
| `iptv.playlists`, `iptv.epg-sources`, `iptv.tuner-profiles`, `iptv.recording-schedules` | the IPTV/DVR configuration tables |
| `iptv.channel-settings` | the admin's per-channel choices: hidden channels (`IsHiddenByAdmin`) and TV/radio overrides (`Kind` + `KindOverriddenByAdmin`), keyed by playlist id + external channel id and applied onto the channels the playlist has loaded |
| `iptv.recordings` | finished DVR recordings (`IptvRecordingSessions` with `Status = Completed`): restored only when the schedule is here and the file is still on disk, and added beside the recordings already here rather than replacing them |
| `discovery.rows`, `discovery.request-servers` | `DiscoveryRowConfigs`, `RequestServers` |
| `users.watch-history` (large) | `UserMediaStates`, `StreamSessions`, `TrackPlayHistory`, `PreservedUserMediaData` (Media Trash archive) |
| `users.ratings` | `UserMediaRatings`, `UserAlbumRatings`, `UserArtistRatings`, `TrackLikes` |
| `users.playlists` | `Playlists`, `PlaylistItems`, `SmartPlaylists` |
| `users.watchlists`, `users.stations` | `UserWatchlistItems`, music `Stations` |
| `users.ai-playlists` | AI playlists a profile asked for and Blends (`GeneratedMixes` of kind `Requested` / `Blend`), songs matched by identity; the mixes and weekly AI playlists Vora rebuilds are left alone |
| `users.requests` | `MediaRequests`, `MediaRequestUsers` |
| `users.external-connections`, `users.channel-favorites` | `UserProviderConnections`, `ProfileChannelFavorites` |
| `podcasts.shows` | every `PodcastShow` (catalog flag + every show a profile follows) |
| `podcasts.listening` | `PodcastSubscriptions`, `PodcastEpisodeProfileStates` |

**Restore order is registration order.** `AddVoraBackups` registers sections so that a section comes after every section its rows point at: libraries first (so profiles' library access lists and collections find their library by id), then users and devices, then collections before smart lists, IPTV playlists before channel settings / tuner profiles / schedules / favorites, schedules before recordings, request servers before requests, the podcast catalog before listening. `BackupSectionsTests` in `Vora.Api.Tests` pins that order — add a pair there when a new section references another.

### What is deliberately not backed up

- **Everything a scan rebuilds** — media items, parts/tracks, artwork, cast, genres, extras, scan-created collections. Restore **Libraries** (or point a rebuilt server at the same folders) and scan. The hand-made changes on those rows are a section of their own (**Metadata edits** below).
- **Derived data** — embeddings, song profiles, the mixes and weekly AI playlists Vora generates (requested AI playlists and Blends are backed up), similar artists and tags, silence/black-frame analysis, audio fingerprints, detected markers, scrub-bar thumbnails, the resized artwork cache. Vora recomputes them.
- **Logs and history the server writes for itself** — email delivery log, AI usage, system metrics, admin notifications.
- **Secrets that should not outlive a server** — refresh tokens, registration / invitation / password-reset / email-change tickets.
- **Short-lived state** — `PendingTasks` (the task queue), IPTV channels themselves (re-synced from the playlist; only the admin's choices about them are kept), DVR sessions that haven't finished (re-created from the schedules), podcast episodes (re-fetched from each feed).
- **Files outside the database** — uploaded artwork, collection images and playlist covers under `StoragePaths:CustomArtwork`, profile pictures, downloaded subtitles, thumbnails, DVR recordings. Back up the data volume for those.

`BackupCoverageTests` (`Vora.Api.Tests`) lists every table in the model either under the section that backs it up or under one of the reasons above, and fails when a table is in neither — a new table has to be given a section or a reason before it ships.

## Restoring onto a rebuilt server

After a rebuild every media item, track, album, artist, library and IPTV channel has a new id. Sections that point at them write `<section>/identities.json` beside their rows (`BackupIdentityFile`, built by `BackupReferenceMapper`), listing a stable identity for every referenced id:

- **Video** — `ContentIdentity.Compute` (the Media Trash key, from the shared `SelectContentIdentitySource` projection that `MediaRepository` also uses), then one key per provider id on its own, then a normalized title + year (series title + year + numbers for seasons and episodes).
- **Tracks / albums / artists** — the album or artist MusicBrainz id when present, then a `MusicNameKey`-normalized name key (artist + album + disc + track number + title; artist + title; name).
- **Libraries** — type + name. **Collections** — TMDB / IMDb / TVDB id, then title. **IPTV channels** — playlist id + external channel id.

Each identity also records its library's name. On restore `BackupReferenceResolver` handles each referenced id: keep it if that row exists here; otherwise try the keys strongest-first against this server's live items (copies in the library with the same name win a tie); otherwise the row is **skipped**. Rows whose profile, user, device, IPTV playlist or podcast is missing are skipped too. After remapping, rows that collide on a table's unique key (e.g. two copies of a film mapping to one) keep the newest. Each section reports `RowsSkipped` plus a plain-English warning per reason (`BackupSkipTally`), e.g. "37 watch-history rows were skipped because their item isn't on this server…"; the restore drawer lists them per section.

Backups written before identities existed still restore: with no `identities.json` the resolver only keeps rows whose ids exist. A file a section didn't write in an older backup (e.g. Media Trash data, ignored duplicates) leaves that table alone rather than emptying it.

Limits worth knowing:

- **Scan first.** Rows for items this server doesn't have yet are skipped, not parked. On a rebuilt server: restore settings, **Libraries** and users, scan the libraries, then restore metadata edits, watch history, ratings, playlists, collections and the like (or simply restore those sections again after the scan).
- **Without the Libraries section** the restore still lines up if each library is recreated with the **same name and type**: items are matched by their own identity wherever they are, and only library-scoped references (profiles' library access, a collection's or smart list's library, dedupe settings) go by library name. A library recreated under another name leaves those pointing nowhere, which is what restoring the definitions avoids — along with re-entering every per-library setting.

## Metadata edits

`library.media-edits` (`MediaEditsBackupSection`, `Sections/MediaEditSections.cs`) writes one `MediaEditRecord` per media item, album or artist that has something made by hand:

- **Locked fields and their values.** `LockedFields` plus, for every lock that names a plain property (title, overview, dates, ratings, artwork URLs, track and disc numbers, …), that property's value, read and written by reflection so a newly lockable field is covered without touching the section. Locks that aren't properties — `Markers`, `Thumbnails`, `Duration` — travel as locks.
- **Hand-edited markers** for items locked with `Markers`.
- **Fixed matches.** `Match` (set by Fix match) also carries the item's TMDB / IMDb / TVDB ids. The result asks the admin to refresh metadata for re-matched titles, since restoring the ids doesn't fetch the details.
- **Uploaded artwork** (`MediaArtwork` with `IsUserUploaded`) and **downloaded subtitles** (`MediaSubtitleTracks` with `IsDownloaded`, restored only when the subtitle file is still on disk and not already attached).

A record is found on this server **by file first** — the item's own file for movies, episodes and tracks; an episode's file for seasons and shows; a track's file for albums and artists — because a fixed match's provider ids are exactly what a fresh scan got wrong. Without a matching file it falls back to the usual identities. Locks are added to the ones already there, never removed, and edits on titles not in the backup are left alone.
- **IPTV recording schedules and channel settings** need the playlist's channels, which arrive on the playlist's first refresh. Restore them again after it, then **DVR Recordings** (which need their schedule, and their file on disk).
- **Podcast progress** maps by the show's feed URL and the episode's feed GUID (then enclosure URL). When the episode isn't fetched yet, a placeholder `PodcastEpisode` is written from the backed-up details; the next feed refresh fills it in by GUID.
- **Library access lists** (`AllowedLibraryIds` on accounts and profiles) are remapped by library name; an id that can't be matched is kept as-is, so a restricted profile stays restricted rather than opening up.
- **Title-only matches** are a last resort for items without provider ids; smart-playlist and smart-list rule JSON is restored as written (ids inside rules are not remapped).

Rows are written by `BackupTableSync.ReplaceAsync`: rows not in the backup are deleted, rows in both are updated in place, new rows are added. Updating in place matters because deletes cascade — the old delete-everything-then-insert wiped every profile's history whenever **User Accounts & Profiles** was restored on its own. Before removing a profile or request server, the section clears the `NO ACTION` references to it (`StreamSessions.UserProfileId`, `MediaRequests.AssignedServerId`).

## Manager

`Vora.Application/Backups/BackupManager.cs` orchestrates create/list/restore/delete/upload and settings. On create it opens one DI scope, resolves the included sections, writes `manifest.json` (per-section uncompressed size and row count) plus the section files into a zip under `StoragePaths:Backups`, self-tests it, then prunes down to `MaxToKeep`.

Restore is **atomic**: one EF transaction from `IBackupTransactionFactory` (`EfBackupTransactionFactory`) covers every selected section on the shared scoped `VoraDbContext`. Any section failure marks earlier results `Restored = false` ("Rolled back because a later section failed.") and rolls back. On success the manager commits, invalidates the size estimate and broadcasts `BackupRestored`. The DataProtection-keys section writes files and is **not** covered by the transaction — an accepted limitation; it is unticked by default.

## Size estimate

`BackupSizeEstimator` (`IBackupSizeEstimator`, singleton) runs every section's `WriteAsync` against `CompressedSizeBackupWriter`, which serializes with the same `BackupJson.Options` as `ZipBackupWriter`, deflates each entry into a byte-counting stream and adds the zip header bytes per entry. The result is the compressed size per section, its row count and the fixed overhead (manifest + end of archive); the Settings tab adds up the ticked sections. A section that throws is reported as `Failed` instead of failing the estimate. Results are cached for five minutes (`refresh=true` bypasses; a restore invalidates). `BackupSizeEstimatorTests` checks the estimate lands within 2% of a real zip.

## Endpoints

`Vora.Api/Endpoints/BackupEndpoints.cs` under `/api/admin/backups` (`AdminOnly`): list, create, `sections`, `sections/estimate?refresh=` (`EstimateBackupSectionSizes`), settings (get/put), per-file manifest, restore, download, delete, multipart upload. The restore body carries `sectionKeys` and `acknowledgeAdminLoss`, which the manager requires when **User Accounts & Profiles** is selected and the calling admin isn't in the snapshot.

## Scheduling + retention

`BackupSettings` (JSON on `ServerSetting.BackupConfigurationJson`): `AutoBackupEnabled`, `Cadence` (`Off` / `Daily` / `Weekly` / `Monthly`), `Hour` / `Minute`, `DayOfWeek`, `DayOfMonth` (1–28), `MaxToKeep`, `OverrideDirectory`, `LastSuccessfulRunUtc`, `ExcludedSectionKeys`.

- **Times follow the server time zone** (`ServerSetting.ScheduleTimeZone`, resolved by `ScheduleClock` like every other scheduled job), not the container clock. `BackupScheduleEvaluator.GetNextRunUtc(settings, afterUtc, zone)` works in the zone's wall time: a time skipped by a spring-forward change runs when the clock jumps, a repeated fall-back hour runs once. `BackupScheduleWorker` ticks every five minutes and calls `IsDue`; the settings VM's `nextScheduledRunUtc` comes from `GetDisplayedNextRunUtc` (now when a run is overdue or the schedule never ran) and `scheduleTimeZone` names the zone.
- **Sections are excluded, not included.** `ExcludedSectionKeys` (null = everything) means a section added in a later release is backed up by default. A legacy `IncludedSectionKeys` list is converted on read against the sections that existed when it was saved (`BackupSectionSelection`). The API still speaks `includedSectionKeys` (null = all); an empty list saves as "all", so the page refuses to save with nothing ticked.

## Real-time events

`BackupCreated` (file name) and `BackupRestored` (`{ fileName, sectionKeys }`) go to the `admins` group through `IClientNotifier` (see `docs/realtime.md`). The Settings tab re-fetches the size estimate on `BackupRestored`.

## Admin Backups page

`Vora.Web/src/pages/Admin/BackupsPage.tsx` with pieces in `components/Admin/Backups/` (`BackupSectionPicker`, `RestoreResultView`, `backupSections.ts` for group order/labels, size formatting and totals).

- **Backups** — zips with time, size, section count, reason, and Restore / Download / Delete. The Restore drawer groups the manifest's sections (Settings, Security, Templates, Library, Live TV & DVR, Discovery & Requests, Podcasts, User data), shows each section's size and row count, warns per destructive section, requires typed `restore` and, for **User Accounts & Profiles**, the admin-loss acknowledgment. The result view lists rows restored and skipped per section with every warning, and stays open until the admin clicks Done.
- **Settings** — schedule (with the server time zone named), retention, override directory, a DataProtection caution when that section is ticked, and the section picker: each section's estimated size, a `large` chip for `CanGrowLarge` sections (Watch History), a live "Estimated backup size: about … MB" total for the ticked boxes, Re-estimate, and a note that it is an estimate of the zip that grows with the library.

## Storage path

`StoragePaths:Backups` (env: `StoragePaths__Backups`). Default `docker-compose.yml` mounts `/app/data/backups` under the existing `./Vora-data:/app/data` volume.

## Things to be careful about

- **DataProtection keys are secrets.** A backup with that section decrypts the saved SMTP password; without it the ciphertext is useless. Treat such archives as credentials (no archive-level encryption is built in).
- **Watch history can be huge.** `StreamSession` dwarfs everything on long-lived servers; the size estimate shows how much it costs.
- **Adding a section.** Implement `IBackupSection` (usually via `EntityTableBackupSection<T>`), register it in `AddVoraBackups` after the sections it references, and if its rows point at media, libraries, collections or channels, record identities through `BackupReferenceMapper` and resolve them on restore. Add its tables to `BackupCoverageTests` and its order to `BackupSectionsTests`. The manager, estimate and Settings picker pick it up automatically.
- **A new lockable field** needs nothing here: `library.media-edits` backs up whatever property a lock names.
- **Adding a table** fails `BackupCoverageTests` until it is backed up or given a reason to be left out. Rows a person makes (rather than a scan or Vora) belong in a section.
