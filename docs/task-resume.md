# Resuming tasks after a restart

The task queue (`ITaskQueueManager`, see `docs/scanning-and-tasks.md`) lives in memory, so a restart used to drop everything queued or running. A deploy mid-way through a subtitle pass, a scan or a thumbnail run meant waiting for the next schedule or scan to start it again. The queue is now journaled to the database and picked up again on startup.

## What is saved

- Every `Queue…` method passes a **recipe** to `Enqueue`: `TaskRecipe.Of(nameof(QueueX), new { …its parameters… })`. The kind is the method name; the arguments are the method's own parameters, camelCased as JSON.
- `TaskRecipes.Restore(queue, recipe, queuedAt)` turns a recipe back into that same call. A restored task is therefore queued exactly as the original was: same display name, resource key, deduplication and follow-up behaviour.
- Library analysis and library thumbnails merge their reason flags when a second request joins a queued task. Their recipes merge the same way (`mergeRecipe`), so the saved task carries the combined reasons. They are restored through `QueueLibraryAnalysis` / `QueueLibraryThumbnails`, which take the flags directly; the bool overloads route through them.
- One-off tasks queued with their own code through `EnqueueTask(name, workItem)` (the per-profile music recommendation refresh) carry no recipe and are not saved. Their schedules re-trigger them.

## Forced runs pick up where they stopped

Restoring a task re-queues it, so the run starts over. A normal run only takes what is still missing (no markers, no thumbnails, no metadata), so it carries on by itself. A **forced** run used to take the whole library again: a restart three days into **Re-analyze all** on a big library threw those three days away.

A forced run now saves **when it was asked for**, as `since` in its recipe (no migration: it sits in `PendingTasks.ArgumentsJson`), and redoes only what was last done before that moment. Work finished after the click counts as done, whoever did it.

| Task | Asked for by | "Done" is |
| --- | --- | --- |
| `QueueLibraryAnalysis` with `Force` | **Re-analyze all**, a forced scan's analysis | `MediaItem.MarkersAnalyzedAt` per movie and per episode. A show is a target while any episode is older; a season skips its fingerprint pass when none is; each episode skips itself (`GetMarkerDetectionTargetIdsAsync`, `SeasonHasPendingMarkerWorkAsync`, the per-item gate, all taking `analyzedBefore`) |
| `QueueLibraryThumbnails` with `Force` | **Regenerate all** | `LastVideoThumbnailGenerationAt` (or an old sprite version) |
| `QueueRefreshLibraryMetadata` forced | **Replace all metadata** | `MediaItem.FullyRefreshedAt` |
| `QueueLibraryAdded` / `QueueLibraryUpdated` / `QueueScanLibrary` forced | a forced scan | `FullyRefreshedAt`, checked per unit in `ScanAndEnrichUnitAsync` (the file scan itself always runs) |
| `QueueRefreshLibraryRatings` | **Refresh ratings** (forced) / **Refresh popularity** (music, always every artist) | `RatingsCheckedAt`; `Artist.PopularityRefreshedAt` |
| music artwork in the two metadata tasks above | forced | `ArtworkCheckedAt` |

`FullyRefreshedAt` is the one column this needed. The existing stamps can't say "this item is finished": `LastMetadataRefresh` is written when a show's own details are applied, before its seasons and episodes, and `RatingsCheckedAt` is skipped while a provider is out of quota, so after OMDb's daily limit most items would never count as done. A forced metadata refresh therefore works **item by item** (`TriggerLibraryEnrichmentAsync`: metadata → artwork → ratings) and stamps `FullyRefreshedAt` only after all three, only on forced runs; a non-forced refresh keeps its three library-wide phases. An item that fails is not stamped and is tried again.

- **Where `since` comes from.** `ForcedSince` in the queue manager: the given time, or now. Analysis and thumbnails hold it per library next to their reason flags (`_analysisSince`, `_thumbnailSince`); a second forced request while one is queued keeps the **later** time, in memory and in the merged recipe (`WithLaterSince`). A request while one runs gets the usual follow-up run with its own time.
- **Tasks saved by 1.0.** Their recipes have no `since`. `TaskJournal` passes each row's `QueuedAt` to `TaskRecipes.Restore`, which uses it for a forced recipe without one. `QueuedAt` is never earlier than the click, so at worst a few items finished just after it are redone; nothing is skipped.
- `TaskRecipe.Time(name)` / `WithTime(name, time)` read and write a UTC ISO-8601 string.

Subtitle pre-extraction needs none of this: it skips every track already in its cache, so a restarted pass only extracts what is left (its counter starts again from 1 against the smaller total).

**Adding a `Queue…` method means adding its recipe and its `TaskRecipes.Restore` case.** `TaskRecipeRoundTripTests` fails otherwise: it queues every method on a real `TaskQueueManager`, restores the recipe onto a substitute, and checks it receives the identical call.

## The journal

`TaskJournal` (`Vora.Infrastructure/Workers/TaskJournal.cs`) implements `ITaskJournal` and is a hosted service.

- `Record(id, name, recipe)` and `Complete(id)` only add to an in-memory queue. They are called on every enqueue and removal, including the thousands of per-file scans a first scan queues, so they never touch the database themselves.
- Every 2 seconds the journal folds the queued entries per task id and writes them to `PendingTasks` in batches of 500. Recording an already-saved task again (a merged recipe) keeps its `Sequence`, so it keeps its place in line. A task recorded and completed within one interval never reaches the table.
- A last flush runs in `StopAsync`. It is registered just before `TaskProcessingWorker`, so it stops after the worker. `docker stop` gives about 10 seconds before a kill, so correctness rests on the periodic flush, not the final one. The worst case is a task that finished in its last two seconds running once more, and tasks skip work that is already done.
- A failed write is kept and retried on the next tick.

## What counts as finished

`RemoveTask(id, interrupted)` decides whether the journal row goes:

- **Finished, failed, or cancelled by someone** → `Complete`, so the row is deleted.
- **Cut short by the server stopping** → `interrupted: true`; the row stays. `TaskProcessingWorker` sets it when the app-stopping token fired and the task's own token did not, whatever the work item threw or returned.
- **Still pending at shutdown** → never removed, so the row stays.

A follow-up run (`rerunIfRunning`) is recorded when it is queued after the first run completes.

## On startup

`TaskJournal.ExecuteAsync` first runs `RestoreAsync`:
1. It reads `PendingTasks` ordered by `Sequence` (which is seeded from the clock, so it keeps increasing across restarts) and logs how many it is resuming.
2. Each row is completed under its old id and restored through `TaskRecipes.Restore`. That queues it under a new id, which records a new row.
3. A kind this version doesn't know (a renamed or removed method) is logged and dropped.

Restored tasks dedupe against whatever startup queues itself, such as the folder watcher's reconciliation or a schedule, through the usual dedupe keys.

`PendingTasks` is operational state. It is not part of backups.
