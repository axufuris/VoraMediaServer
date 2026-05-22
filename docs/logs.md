# Server logs

Vora captures every `Microsoft.Extensions.Logging` entry into an in-memory ring buffer + a daily-rolling file, exposes filtering/search/runtime-level controls through an admin page, and live-streams new entries to connected admins via SignalR.

## Capture pipeline

All in `Vora.Application/Logging/`:

- **`InMemoryLogBuffer`** — thread-safe ring buffer (default 10,000 entries, configurable via `Logging:Vora:BufferCapacity`). Drops oldest on overflow. Raises an `EntryAppended` event consumed by the SignalR broadcaster.
- **`LogFileSink`** — `IHostedService` writing to `StoragePaths:Logs/vora-yyyyMMdd.log`. Backed by a bounded `Channel<LogEntry>` (drop-oldest) so logging never blocks the request path. Prunes files older than `Logging:Vora:RetentionDays` (default 14).
- **`LogLevelOverrideProvider`** — runtime per-category overrides with longest-prefix match. The default level is read from `Logging:LogLevel:Default` at startup and can be changed at runtime from the admin Logs page.
- **`VoraLoggerProvider`** — `ILoggerProvider` that fans every entry into the buffer + file sink, gated by the override provider. Registered in `AddVoraLogging` (`Vora.Api/Extensions/ServiceRegistrationExtensions.cs`) before `builder.Build()` so it captures startup logs.

The framework filter for `VoraLoggerProvider` is forced to `Trace` so the override layer does the real gating — otherwise the global `Logging:LogLevel` would block Debug/Trace before our provider sees them.

## Query + control

`Vora.Application/Logging/LogManager.cs` exposes:

- `Query(LogQueryRequest)` — filter by level set, category prefix (case-insensitive), free-text (over message + exception), since/until, `beforeId` paging.
- `Export(request, "txt" | "json")` — full filtered set as a download stream.
- `GetLevelState()` / `SetLevel(category, level)` / `ClearOverride(category)` — manage runtime overrides.
- `GetKnownCategories()` — the distinct categories currently in the buffer (for the UI's autocomplete).

Endpoints (`Vora.Api/Endpoints/LogEndpoints.cs`) live under `/api/admin/logs` and are gated by the `AdminOnly` policy.

## Live tail

`LogBroadcastHostedService` subscribes to `InMemoryLogBuffer.EntryAppended` and coalesces entries into ~150 ms windows (or 200-entry batches), broadcasting them as `LogEntryBatch` to the `admins` SignalR group via `IClientNotifier.NotifyLogEntriesAsync`. See `docs/realtime.md` for the broader hub conventions.

## Admin Logs page

`Vora.Web/src/pages/Admin/LogsPage.tsx`. Sticky `PageHeader` with action buttons (Pause/Follow, Export `.txt` / `.json`, Log Levels drawer). Sticky filter bar: level chips (multi-select), category input, search input, time range presets (5m / 1h / 24h / Custom). Each row is a single line; rows with exceptions are expandable and reveal the formatted stack trace.

Follow mode auto-scrolls when new entries arrive. If the admin scrolls up or hits Pause, a floating "N new entries below ↓" pill appears.

The **Log Levels** drawer (slide-over at `z-[200]`) shows the default level, the list of active overrides (each with a per-row dropdown + Reset), and an "Add override" form with category autocomplete sourced from `GetKnownCategories()`.

## Storage paths

`StoragePaths:Logs` (env: `StoragePaths__Logs`) selects the file-sink directory. The default `docker-compose.yml` mounts `/app/data/logs` under the existing `./Vora-data:/app/data` volume so files survive container rebuilds.

## Things to be careful about

- **Secrets in log messages.** The viewer page renders raw messages verbatim. Don't log API keys or passwords — and the plugin-settings env seeder explicitly redacts values so they never reach this pipeline (see `docs/plugins.md`).
- **Bumping levels to Trace can be expensive.** The override provider gates each entry, but very chatty categories at Trace will still hit the ring buffer + file sink. Use overrides for short-lived debugging, not as a default.
- **Ring buffer is process-local.** Restart the container and the buffer empties. The file sink is the persistent record.
