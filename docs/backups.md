# Backup & restore

Vora can create configuration snapshots (and user-data snapshots) as zip files on disk, restore them selectively, and run automatically on a schedule. The system is built on a pluggable section model so adding a new slice of state to the backup is a one-file change.

## Sections

Every slice of state implements `IBackupSection` (`Vora.Application/Backups/IBackupSection.cs`):

```csharp
public interface IBackupSection
{
    string Key { get; }                       // "settings.server"
    string DisplayName { get; }               // "Server Settings"
    BackupSectionGroup Group { get; }         // Settings | Templates | Library | Iptv | Discovery | Security | UserData
    bool RequiresExplicitConfirm { get; }
    string? DestructiveWarning { get; }
    Task WriteAsync(IBackupWriter writer, CancellationToken ct);
    Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct);
}
```

Concrete implementations live in `Vora.Infrastructure/Backups/Sections/` (because they touch `VoraDbContext`). A shared `EntityTableBackupSection<TEntity>` base in the same folder covers the common "dump a DbSet to JSON" pattern — most sections are 5–10 lines.

Currently in the box: server settings, plugin settings, DataProtection keys (filesystem, not DB), email templates, client template schedules, overlay templates, smart lists, dedupe rules, IPTV playlists / EPG sources / tuner profiles / recording schedules, discovery row configs, request servers, and the user-data group (users + profiles + access schedules, devices + per-device settings, watch history, ratings, external connections).

User-data sections set `RequiresExplicitConfirm = true` and carry a `DestructiveWarning`. Restore UI uses these flags to surface warnings and keep destructive sections unchecked by default.

## Manager

`Vora.Application/Backups/BackupManager.cs` orchestrates create/list/restore/delete/upload. On create it opens a single DI scope, resolves all `IBackupSection` registrations, writes a `manifest.json` plus per-section files into a zip under `StoragePaths:Backups`, self-tests the zip by reading the manifest back, then prunes older zips down to `MaxToKeep`.

Restore is **atomic**. The manager resolves `IBackupTransactionFactory` (implemented in `Vora.Infrastructure/Backups/EfBackupTransactionFactory.cs`) and opens a single EF Core transaction up front. All section providers share the same scoped `VoraDbContext`, so their `SaveChangesAsync` calls enrol in the transaction. Any section failure marks every prior `RestoreSectionResult` as `Restored = false` with a "Rolled back because a later section failed." warning and rolls back. On success, the manager commits and emits a SignalR `BackupRestored` broadcast.

The DataProtection-keys section writes XML files to `StoragePaths:DataProtection` and is **not** covered by the DB transaction. If DB restore rolls back after the key files were already written, the disk state diverges. This is an accepted limitation; the DP keys section is normally unchecked by default.

## Endpoints

`Vora.Api/Endpoints/BackupEndpoints.cs` under `/api/admin/backups` (`AdminOnly`): list, create, sections, settings (get/put), per-file manifest preview, restore, download (zip), delete, multipart upload. Restore body carries `sectionKeys: string[]` plus an `acknowledgeAdminLoss: bool` that the manager checks when the **Users & Profiles** section is selected — if the calling admin's account id isn't in the incoming snapshot, the restore is refused unless this flag is true.

## Scheduling + retention

`BackupSettings` (stored as JSON on `ServerSetting.BackupConfigurationJson`):

- `AutoBackupEnabled` — master toggle.
- `Cadence` — `Off` / `Daily` / `Weekly` / `Monthly`.
- `Hour` / `Minute` (local time), `DayOfWeek`, `DayOfMonth` (1–28 to avoid month-end edges).
- `MaxToKeep` — older backups beyond this count are auto-pruned after each successful create.
- `OverrideDirectory` — opt-in path override, must be inside the container's filesystem (typically another mounted volume).
- `IncludedSectionKeys` — `null` means "all sections", otherwise only listed keys are included in scheduled and manual creates. The Settings tab UI surfaces a grouped picker so admins can omit e.g. **Watch History** to keep backups small.
- `LastSuccessfulRunUtc` — bookkeeping the schedule worker reads on each tick.

`BackupScheduleWorker` (a `BackgroundService` independent of `ScheduledJobWorker`) ticks every five minutes and calls `BackupScheduleEvaluator.GetNextRunUtc` to decide whether to fire.

## Real-time events

- `BackupCreated` — broadcast to the `admins` group after a successful create. Payload: file name.
- `BackupRestored` — broadcast after a successful restore. Payload: `{ fileName, sectionKeys: string[] }`.

Both are wired through `IClientNotifier` (`Vora.Application/Analysis/IClientNotifier.cs`) following the convention in `docs/realtime.md`.

## Admin Backups page

`Vora.Web/src/pages/Admin/BackupsPage.tsx`. Two tabs:

- **Backups** — list of zips with timestamp/size/section-count/"manual" vs "auto" chip and per-row Restore/Download/Delete. Toolbar has Create Backup Now + Upload Backup. The Restore drawer loads the manifest, groups sections by `BackupSectionGroup`, renders destructive warnings per section, requires typed `restore` confirmation, and additionally requires the admin-loss acknowledgment when **Users & Profiles** is selected. Live updates via `useSignalREvent('BackupCreated' | 'BackupRestored')`.
- **Settings** — Two-column layout (`grid-cols-5`, 2/5 schedule + 3/5 sections-picker). Schedule card holds the toggle, cadence + time + day-of-week / day-of-month, retention, optional override directory, and the DataProtection warning callout. Section picker card lists every available section grouped, with quick All / None links and a small `selected/total` counter; sections flagged destructive carry a `large` chip so admins can spot user-data sections at a glance.

## Storage path

`StoragePaths:Backups` (env: `StoragePaths__Backups`). Default `docker-compose.yml` mounts `/app/data/backups` under the existing `./Vora-data:/app/data` volume.

## Things to be careful about

- **DataProtection keys are secrets.** Backups including the DP keys section contain the keys that decrypt the saved SMTP password. Store backup files like passwords. The UI surfaces a one-line caution on the Settings tab; the section itself sets `RequiresExplicitConfirm` so it stays unchecked by default on restore.
- **Watch history can be huge.** On long-lived servers `StreamSession` dwarfs everything else. Skip it from `IncludedSectionKeys` if you only want config snapshots — the Settings UI flags it with a `large` chip.
- **Admins are gated against locking themselves out.** Restoring **Users & Profiles** without the calling admin's account id in the snapshot returns an error unless `acknowledgeAdminLoss=true` is set in the restore body. The frontend exposes this as a separate checkbox you must tick in the Restore drawer.
- **Adding a new section.** Implement `IBackupSection` in `Vora.Infrastructure/Backups/Sections/`, register it as a scoped `IBackupSection` in `AddVoraBackups` (`ServiceRegistrationExtensions.cs`). That's it — the manager will include it in creates automatically and the Settings UI will pick it up from `GET /api/admin/backups/sections`.
