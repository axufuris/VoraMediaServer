# Scanning & background tasks

How new media gets ingested and how long-running work is queued, named, and cancelled. Touches `Vora.Application/Tasks`, `Vora.Infrastructure/Workers`, `Vora.Infrastructure/FileSystem`, and the Local scanner/ingestion in `Vora.Plugins`.

## Background task queue

`ITaskQueueManager` (`Vora.Application/Tasks/TaskQueueManager.cs`) is an in-memory queue over an unbounded `Channel<QueuedTaskDto>`. Every long-running operation (library scan, metadata/artwork/ratings refresh, analysis, overlays, thumbnails, collection sync, EPG sync, embeddings) is enqueued through a `QueueXxx` method. `TaskProcessingWorker` (`Vora.Infrastructure/Workers/TaskProcessingWorker.cs`) is the `BackgroundService` consumer.

### Concurrency + resource keys

The worker is a **bounded, key-aware dispatcher** — not a single sequential loop. It runs up to **`MaxConcurrency` (3)** tasks at once, but **never two tasks that share a `ResourceKey`**:

- Each task has a `ResourceKey`. Unkeyed tasks (via `EnqueueTask(...)` with no key) get a **unique** key, so they're always eligible to run alongside others.
- **Every heavy job on one library is keyed `library:{id}`** (via `LibraryKey(id)`) — scan, update, refresh-metadata, analyze, watcher file-ingest, video-thumbnails. So a scan and a refresh of the *same* library **serialize** (they'd otherwise race on its rows), while **different libraries run concurrently** and a quick unrelated task (EPG sync, single-item refresh) isn't blocked behind a long scan.
- The cap is deliberately low because heavy jobs **already parallelize internally** (scan/analysis/overlays run several items at once); a high cap would over-subscribe CPU + providers.
- FIFO within a key: a key-blocked task stays pending and starts when its key frees. On shutdown the dispatcher **awaits in-flight tasks** so each removes itself cleanly.

Each task carries:

- `Id`, `Name`, `Status` — surfaced to the admin Tasks UI via `GetAllTasks()` + the `TasksUpdated` SignalR push.
- `WorkItem(CancellationToken, IServiceProvider)` — the actual work; gets a fresh DI scope per run.
- `ResourceKey` — mutual-exclusion group (see above).
- `NameResolver(IServiceProvider) -> string?` — optional; resolves a friendly display name at run time (see below).

`QueueXxx` methods only enqueue (fire-and-forget). Heavy logic lives in the managers the work item resolves from the scope; the queue manager just wires them together.

### Per-item progress

Long tasks that iterate a set of items (metadata / artwork / ratings refresh, analysis + marker phases) report **which item they're on** so the admin UI shows live detail instead of a static "Running". `ITaskProgressReporter` (`Vora.Plugins/Interfaces/ITaskProgressReporter.cs`) is resolved from the work item's scope; `TaskProgressReporter` (`Vora.Application/Tasks/TaskProgressReporter.cs`) forwards to `ITaskQueueManager.ReportProgress`, and the detail rides the existing `TasksUpdated` push — **no new SignalR event**. Managers that don't run inside a task use `NullTaskProgressReporter` (no-op), so the same manager method works from both an endpoint and a queued task. New iterating work items should take `ITaskProgressReporter` and call it per item.

## Full-library workflow order (parallel per-unit scan + enrich)

`RunFullLibraryWorkflowAsync` (in `TaskQueueManager`) is the ordered pipeline a library add / full rescan runs. A **unit** is one show (all its episode + extra files) or one movie (its file(s) + extras). The pipeline:

1. **Discover units** — `ILibraryManager.DiscoverScanUnitsAsync` → the scanner enumerates the library's new (non-excluded) files and groups them: TV by `GetTvShowFolderName`, movies by parent directory. Returns a `List<ScanUnit>` (label + file paths).
2. **Scan + enrich each unit, in parallel** — `Parallel.ForEachAsync` over the units at `Math.Clamp(ProcessorCount, 2, 6)` degree. Each unit runs `ILibraryManager.ScanAndEnrichUnitAsync`, which:
   - Opens **one DI scope per unit** and resolves the scanner **and** the metadata manager from it — so the scan and the enrich share the **same `DbContext`**. The scanner ingests the unit's files (creating the show/seasons/episodes or movie), then `TriggerMediaItemMetadataRefreshAsync` → `...ArtworkRefreshAsync` → `...RatingsRefreshAsync` fill its metadata/posters onto **the very rows the scan just created**. This shared scope is what keeps season posters from being clobbered (see the note below). A poster therefore appears per show/movie *as the scan runs*, and several fill at once.
   - **Episodes enrich on the show's own scope (sequentially), NOT in child scopes.** For a TV show, `TriggerMediaItemMetadataRefreshAsync` refreshes each episode via `RefreshMetadataAsync` on the same scope, so they all share the provider's per-show episode-list cache — the bulk episode list (`series/{id}/episodes/default/{lang}`, up to 5 pages) is fetched **once** and every episode reads from it. **Do not** enrich episodes in their own child scopes: a fresh scope means a fresh provider with an empty cache, so each episode re-fetches the whole list — a ~Nx request storm (a 50-episode show goes from 5 fetches to 250) that saturates the provider and stalls the scan on its tail. A failing episode is caught + logged so it can't abort the rest of the show.
   - **Per-unit timeout.** Each unit's `ScanAndEnrichUnitAsync` is raced against a 5-minute budget (`Task.WhenAny`). If a unit overruns (an unresponsive provider, a title that trips a slow path), it's abandoned — logged, left to finish in the background with its exception observed — so one show can never freeze the whole library and starve the deferred passes (overlays/analysis). This is a safety net, not the happy path.
   - One failing unit is caught and skipped — it doesn't abort the library.
   Music libraries (no units) fall back to the whole-library `TriggerLibraryFolderAndFileScanAsync` + `TriggerLibraryEnrichmentAsync`.

   **The deferred passes (steps 3–6) each run in their own try/catch** (`RunStepAsync`) so one failing pass can't starve the ones after it — a library must never end up with, e.g., no analysis just because overlay generation threw. `OperationCanceledException` still propagates and stops the whole workflow. (Historically these were an unguarded chain, so a slow/interrupted scan+enrich or a throw in one pass meant overlays/actors/analysis silently never ran.)
3. **Enrichment safety net** — `TriggerLibraryEnrichmentAsync` (non-force) catches anything a unit missed; already-enriched items are skipped, so it's cheap.
4. **Actor metadata** — one global, whole-DB pass. Must stay deferred/global, not per-unit (it's not library-scoped).
5. **Analyze media + detect markers** — the heavy FFmpeg passes (`TriggerLibraryFileAnalysisAsync`, then `TriggerLibrarySilenceDetectionAsync`), plus video-preview thumbnails on their own schedule. **File analysis is parallel and part-guarded**: it runs items through `Parallel.ForEachAsync` (each in its own scope via `AnalyzeMediaFileAsync`), and `RunFileAnalysisAsync` ffprobes a `MediaPart` only if it was never analyzed (`MediaPart.LastAnalyzedAt == null`) or its file changed on disk (size differs). So a re-scan of an unchanged library costs one cheap file-size check per part rather than a full re-probe. **The progress total is pre-filtered, not the whole library**: the non-force library triggers resolve their work list from repository target queries — file analysis via `GetFileAnalysisTargetIdsAsync` (items with any part `LastAnalyzedAt == null`), marker detection via `GetMarkerDetectionTargetIdsAsync` (`MarkersAnalyzedAt == null`), video thumbnails via `GetVideoThumbnailTargetIdsAsync` (missing or a stale sprite version) — so the "(n/total)" you see reflects items that actually need the pass, not every item in the library. A force run still targets everything. Each library trigger also carries a `dedupeKey` so re-clicking while one is already queued doesn't stack a duplicate. Note: this "Analyzing media" pass is the **track/duration probe** the player needs (audio/video/subtitle streams, HDR, bitrate) — it is **not** the intro/credit/thumbnail detection. **Marker detection short-circuits** when the library has both `EnableIntroDetection` and `EnableCreditsDetection` off — it returns before the per-item loop instead of iterating the whole library (and showing "Detecting intro/credit markers …") only to skip each item.
6. **Poster overlays** — `RunLibraryOverlaySyncAsync` composites badges onto the posters the enrich step set, **in parallel** (each item in its own scope via `GenerateOverlaysForMediaAsync`). This runs **LAST, after analysis** — the audio-codec / HDR badges read the `MediaAudioTracks` / `MediaVideoTracks` that analysis populates, so overlaying before analysis left posters with only scan-time data (resolution) and no audio/HDR badge. Markers run before it too, so the stinger badge is available.

**Phase timing is logged.** Each run logs (Information level) the wall time of every phase: `Scan+enrich … {Units} units in {Wall}s ({N}-way, avg {Avg}s/unit). Slowest: …`, one line per deferred phase (`Library workflow phase '…' took {Wall}s`), and a final `Full library workflow … completed in {Wall}s`. Read these to find the real bottleneck before optimizing — the per-unit "slowest" list points straight at the shows to look at.

**Bulk enrichment suppresses per-episode SignalR.** During a show's episode loop, `RefreshMetadataAsync(epId, notify: false)` skips the per-episode `NotifyItemAndParentsAsync` fan-out (which would re-notify the same show + seasons once per episode); the show is notified **once** after the loop. Cuts tens of thousands of redundant broadcasts on a large first scan.

### Why the shared scope matters (season-poster regression)

Season posters are written by `ProcessTvSeasonsAsync` during the **show's** metadata refresh, onto the season rows. An earlier design ran the scan in one `DbContext` and enrichment in a *separate* context while the scanner's context was still open on those rows — a concurrent-context clobber that left season posters null. Keeping a unit's scan **and** enrich in **one scope** removes that entirely; separate units use separate scopes, so parallelism stays isolated. **Do not** reintroduce a design where enrichment runs in a different context from the scan that created the rows.

### Parallel-safe shared rows (`ReferenceWriteGate`)

Actors, genres, companies, countries, networks, and collections are shared across items, so two parallel units could both "read missing → insert" the same row and collide. `ReferenceWriteGate` (a **singleton** `SemaphoreSlim`, injected into `MetadataMappingService`) serializes just the read-create-**commit** of those shared rows inside `ApplyTextMetadataAsync` — committing inside the gate makes the row visible to the next worker before it reads. The metadata **network fetch stays outside the gate**, so it runs fully in parallel; only the brief shared-row write is serial.

## Cancellation (must stay correct)

Each enqueued task gets its own `CancellationTokenSource` in `_taskTokens`. The worker links it with the app-lifetime `stoppingToken` and passes **that linked token** to the work item:

```csharp
var taskToken = _taskQueue.GetTaskCancellationToken(task.Id);
if (taskToken == null || taskToken.Value.IsCancellationRequested) { /* skip queued-then-cancelled */ }
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, taskToken.Value);
await task.WorkItem(linkedCts.Token, scope.ServiceProvider);
```

Rules that make cancel actually work — don't regress these:

- The worker passes the **linked** token, never bare `stoppingToken`. (The original bug: it passed `stoppingToken`, which `CancelTask` never touches, so cancel did nothing.)
- `CancelTask` cancels the source but **keeps** the `_taskTokens` entry so a running task's linked token fires and a still-queued task is observed as cancelled and skipped. `RemoveTask` disposes it in the worker's `finally`.
- Multi-step work items must observe the token. `RunFullLibraryWorkflowAsync` calls `ct.ThrowIfCancellationRequested()` between steps so a cancel stops at the next boundary instead of running to completion. New long work items should do the same (or thread `ct` into the calls).

## Task names (no raw GUIDs)

A task can show a friendly name even when the caller didn't pass one. `EnqueueTask(name, workItem, nameResolver?)` stores an optional resolver; the worker runs it (in the task's scope) before marking the task running and calls `UpdateTaskName`. Helpers `LibraryLabel(id, "…: {0}")` and `MediaLabel(id, "…: {0}")` look up the library `Name` / media `Title` by id. Library/media `QueueXxx` methods attach a resolver when no name was supplied (e.g. watcher-triggered scans), so the UI never shows `Scan Library: <guid>`.

## Folder watcher → per-file ingest

`FolderWatcherService` (`Vora.Infrastructure/FileSystem`) starts a watcher provider per library with real-time watching enabled (restarted on boot by `StartupWatcherService`). On a new supported video file (`.mkv/.mp4/.avi/.m4v`) it debounces 5s then calls:

```csharp
taskQueue.QueueScanNewFile(libraryId, filePath);
```

`QueueScanNewFile` ingests **just that one file** — NOT a full library scan. This is deliberate: copying a season folder with N episodes produces N cheap per-file ingests, each doing its own distinct file, so there's no scan flood and no dedupe needed. (The old behaviour queued a full `QueueScanLibrary` per file → N redundant full scans.)

**Exclude filters are enforced watcher-side, on both adds and deletes.** Before enqueuing, `FolderWatcherService` checks the file name against the library's `ExcludeFilters` (e.g. `.TDARR`, transcoder working dirs) via the shared `IsExcludedAsync` helper. A single-file scan also re-checks, but the watcher must reject first — otherwise the task is queued (and shows up in the UI) before the scanner no-ops it. The **delete** path checks too: an excluded file was never ingested, so its deletion must not queue an `Auto-Cleanup` (`QueueRemoveOrphanedMedia`) task — otherwise a transcoder churning `*.TDARR` temp files floods the queue with no-op cleanups.

The per-file work item:

1. `ILibraryManager.TriggerFileScanAsync(libraryId, filePath)` → dispatches by library type to the scanner's single-file method and returns a `ScanFileResult { MediaItemId, ParentShowId, NewSeasonCreated }`.
2. Runs targeted analysis + metadata/artwork/ratings refresh for the ingested leaf item (episode/movie). The metadata refresh links the item's own **cast**, so actors appear on the item immediately.
3. **Only if a new season was created**, refreshes the parent show's metadata once (see next section).
4. Overlays + silence detection for the item. The per-file path deliberately does **not** call the global `TriggerActorMetadataRefreshAsync` — that fetches up to 50 actors from TMDB per call, which is crippling when a whole library is ingested one file at a time. Actor **entity** metadata (bios, photos on the actor detail page) is enriched by the nightly scan and the full-library workflow instead; the cast list itself is already set in step 2.

## Single-file scanner + new-season metadata

`ILocalMediaScannerProvider` gained `ScanMovieFileAsync(libraryId, filePath)` and `ScanTvFileAsync(libraryId, filePath)`. They reuse the same parsing as the full-library scan — the per-file body was factored into `IngestMovieFileAsync` / `IngestTvFileAsync` and the regex setup into `BuildMovieRegexes` / `BuildTvRegexes`, so library and single-file scans share one code path. Both skip a path already in the library.

A **season's** poster and (metadata) fields come from the parent **show's** metadata mapping, not from scanning the episode files. So when an episode is added under a season that didn't exist yet, the new season would otherwise have no poster. To fix this without re-mapping the show on every file:

- `IMediaIngestionService.SeasonExistsAsync(tvShow, seasonNumber)` is checked **before** `EnsureSeasonAsync`, so `IngestTvFileAsync` knows whether it created a new season (`ScanFileResult.NewSeasonCreated`).
- `QueueScanNewFile` refreshes the parent show's metadata **exactly once** for a genuinely new season. Because the single consumer runs tasks sequentially, the first file of a new season sees `NewSeasonCreated = true` and the rest see the season already exists — so a 20-episode season copy maps the show once, not 20 times.

Manual "Refresh metadata" (force) still re-maps everything; this just makes the common add-a-season case self-heal.

### Duplicate-item prevention

`IMediaIngestionService.EnsureMovieAsync` must not create a second `MediaItem` for a movie that's already in the library under a differently-cased or metadata-rewritten title. It resolves an existing item by, in order: **external id** (`GetMovieIdByExternalIdAsync` — TMDB, then IMDB, within the library), then **normalized title + year** (`GetMovieIdByTitleAndYearAsync`, which lower-cases and strips punctuation via `NormalizeTitle` and compares in memory). Only if both miss is a new item created. The old code compared the raw, case-sensitive `Title`, so a file scanned before metadata mapping and one scanned after (metadata rewrote the title) produced two rows sharing the same TmdbId/ImdbId.

### Editions live on the part

Each `MediaPart` carries its own `Edition` (Director's Cut, IMAX, …), parsed from the filename during ingest. `AddMediaPartAsync` sets `part.Edition` and calls `SyncItemEditionFromPartsAsync`, which denormalizes `MediaItem.Edition` from the **best (highest-resolution) part** for display/sort. Adding a part also clears `MediaItem.LastOverlayGeneratedAt` (so the poster overlay regenerates against the new best part — see `docs/artwork-image-cache.md`) and `MediaItem.MissingSince` (a re-added file un-trashes the item). At play time the client picks a part through the "Version" selector; the choice flows as `StartStreamRequest.MediaPartId` → `BestPathDecisionManager` → `StreamSession.MediaPartId`.

### Artwork refresh only fetches what's missing

A **non-force** library artwork refresh (`MetadataManager.TriggerLibraryArtworkRefreshAsync`) now fetches only items missing a poster (`GetMediaIdsMissingArtworkAsync`, `PosterUrl == null`) instead of re-hitting every item. Force still refetches everything.

## Soft-delete & Media Trash

Removing a file from disk no longer hard-deletes its library item. When the folder watcher sees a video file disappear (or a scan finds all of an item's parts gone), the video `MediaItem` is **marked missing** by stamping `MissingSince` (UTC) instead of being deleted; re-adding the file clears `MissingSince`. Missing items are filtered out of every client read path (`QueryableExtensions`, `SmartPlaylistEvaluator`, `RecommendationRepository`, `CollectionRepository`, `UserMediaStateRepository`) so they vanish from clients but survive in the DB with their metadata, ratings, and watch-state intact.

- **Music `Track` still hard-deletes** — the soft-delete tombstone is video-only. Don't assume symmetry.
- **Admin Media Trash page** (`pages/Admin/MediaTrashPage.tsx`, `mediaTrashService.ts`) lists trashed items via `GET /media/trash` (→ `TrashMediaItemVM[]`), restores with `POST /media/trash/{id}/restore`, and permanently removes with `DELETE /media/trash/{id}`.
- **Auto-purge**: the daily maintenance job in `ScheduledJobWorker` runs at `NightlyScanTime` and, when `ServerSetting.EnableTrashAutoPurge` is on, calls `MediaManager.PurgeExpiredTrashAsync` to permanently delete items whose `MissingSince` is older than `MissingMediaRetentionDays`.
- **User data outlives the purge**: a permanent purge first archives per-profile ratings + watch-state into `PreservedUserMediaData`, so re-adding the same content later restores them. See `docs/auth-and-devices.md`.

## Episode counts are live

`SeasonVM.EpisodeCount` (and `SeasonDetailsVM`) project from the actual library episodes (`Episodes.Count`) rather than the stored TMDB/TVDB `Season.EpisodeCount` metadata field. A season therefore reports the number of episodes actually present even before its metadata has been fetched. (`Season.EpisodeCount` is still populated from metadata but is no longer what the clients display.)
