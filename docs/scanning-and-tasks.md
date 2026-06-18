# Scanning & background tasks

How new media gets ingested and how long-running work is queued, named, and cancelled. Touches `Vora.Application/Tasks`, `Vora.Infrastructure/Workers`, `Vora.Infrastructure/FileSystem`, and the Local scanner/ingestion in `Vora.Plugins`.

## Background task queue

`ITaskQueueManager` (`Vora.Application/Tasks/TaskQueueManager.cs`) is an in-memory queue over an unbounded `Channel<QueuedTaskDto>`. Every long-running operation (library scan, metadata/artwork/ratings refresh, analysis, overlays, thumbnails, collection sync, EPG sync, embeddings) is enqueued through a `QueueXxx` method. `TaskProcessingWorker` (`Vora.Infrastructure/Workers/TaskProcessingWorker.cs`) is the **single** `BackgroundService` consumer — tasks run **sequentially**, one at a time.

Each task carries:

- `Id`, `Name`, `Status` — surfaced to the admin Tasks UI via `GetAllTasks()` + the `TasksUpdated` SignalR push.
- `WorkItem(CancellationToken, IServiceProvider)` — the actual work; gets a fresh DI scope per run.
- `NameResolver(IServiceProvider) -> string?` — optional; resolves a friendly display name at run time (see below).

`QueueXxx` methods only enqueue (fire-and-forget). Heavy logic lives in the managers the work item resolves from the scope; the queue manager just wires them together.

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

The per-file work item:

1. `ILibraryManager.TriggerFileScanAsync(libraryId, filePath)` → dispatches by library type to the scanner's single-file method and returns a `ScanFileResult { MediaItemId, ParentShowId, NewSeasonCreated }`.
2. Runs targeted analysis + metadata/artwork/ratings refresh for the ingested leaf item (episode/movie).
3. **Only if a new season was created**, refreshes the parent show's metadata once (see next section).
4. Actor metadata, overlays, silence detection for the item.

## Single-file scanner + new-season metadata

`ILocalMediaScannerProvider` gained `ScanMovieFileAsync(libraryId, filePath)` and `ScanTvFileAsync(libraryId, filePath)`. They reuse the same parsing as the full-library scan — the per-file body was factored into `IngestMovieFileAsync` / `IngestTvFileAsync` and the regex setup into `BuildMovieRegexes` / `BuildTvRegexes`, so library and single-file scans share one code path. Both skip a path already in the library.

A **season's** poster and (metadata) fields come from the parent **show's** metadata mapping, not from scanning the episode files. So when an episode is added under a season that didn't exist yet, the new season would otherwise have no poster. To fix this without re-mapping the show on every file:

- `IMediaIngestionService.SeasonExistsAsync(tvShow, seasonNumber)` is checked **before** `EnsureSeasonAsync`, so `IngestTvFileAsync` knows whether it created a new season (`ScanFileResult.NewSeasonCreated`).
- `QueueScanNewFile` refreshes the parent show's metadata **exactly once** for a genuinely new season. Because the single consumer runs tasks sequentially, the first file of a new season sees `NewSeasonCreated = true` and the rest see the season already exists — so a 20-episode season copy maps the show once, not 20 times.

Manual "Refresh metadata" (force) still re-maps everything; this just makes the common add-a-season case self-heal.

## Episode counts are live

`SeasonVM.EpisodeCount` (and `SeasonDetailsVM`) project from the actual library episodes (`Episodes.Count`) rather than the stored TMDB/TVDB `Season.EpisodeCount` metadata field. A season therefore reports the number of episodes actually present even before its metadata has been fetched. (`Season.EpisodeCount` is still populated from metadata but is no longer what the clients display.)
