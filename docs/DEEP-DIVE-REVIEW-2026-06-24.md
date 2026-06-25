# Vora Deep-Dive Review — 2026-06-24

Scope: full backend (`Vora.Api`, `Vora.Application`, `Vora.Infrastructure`, `Vora.Plugins`) and the web client (`Vora.Web`). Focus: bugs, resource/memory leaks, cleanup, and web-client issues. Findings verified against source; line numbers approximate.

## Overall health

The codebase is in good shape. Verified clean across the board: `IHttpClientFactory` everywhere (no static/`new HttpClient`), no `.Result`/`.Wait()`/`async void`, `AsNoTracking()` on read paths, no nullability suppressions (`!`/`#pragma`) outside generated migrations, claims access only through `AuthExtensions`, every `BackgroundService` opens an `IServiceScopeFactory` scope before touching DbContext (no captive-dependency bug), the SignalR hub cleans up groups on disconnect and sends only IDs/VMs. The player layer — the highest-risk part of the web client — correctly destroys every hls.js instance, releases video `src`, clears intervals, and revokes object URLs.

The real issues cluster in the **DVR / IPTV recording path** and a few unbounded singletons. The DVR recording service was flagged independently by multiple passes and is the top priority.

## HIGH

**1. Anonymous music streaming — `Vora.Api/Endpoints/MusicEndpoints.cs:59,787` (security)**
`/tracks/{trackId}/stream` is `.AllowAnonymous()` and `StreamTrackAsync` does no token check — it resolves the path with `MusicAccessFilter.Unrestricted` and streams the file to anyone who knows the track GUID. Every other media-stream endpoint (HLS, timeshift) gates on a signed streaming token; this is the lone outlier. Only protection is GUID unguessability. *Fix: require a signed stream token (mirror the HLS pattern) or `RequireAuthorization()`.*

**2. DVR FFmpeg `Process` never disposed — `Vora.Application/Iptv/DvrRecordingService.cs:121-142`**
Each reconnect iteration does `new Process` and at most `Kill()`s it; it's never `Dispose()`d (and on the success-path `break` it's neither killed nor disposed). Long recordings with reconnects leak process/pipe handles. *Fix: `using var process = new Process { ... }` inside the loop, or dispose in `finally`.*

**3. DVR `CancellationTokenSource` never disposed — `DvrRecordingService.cs:22,~155`**
`ActiveRecording.Cts` is removed from the dictionary but `Dispose()` is never called. *Fix: dispose the CTS in the `finally` of `RecordingLoopAsync`.*

**4. `DvrPostProcessingWorker` dies on first exception/shutdown — `Vora.Infrastructure/Workers/DvrPostProcessingWorker.cs:30-40`**
The tick loop has no `try/catch` and no `OperationCanceledException` handler. Any throw from `ProcessCompletedRecordingsAsync()` — or the cancellation raised by `WaitForNextTickAsync` on shutdown — escapes `ExecuteAsync` and permanently kills the worker (post-processing silently stops). `DvrStorageMonitorWorker`/`BackupScheduleWorker` already do this correctly. *Fix: try/catch-log inside the loop, catch `OperationCanceledException` around it.*

**5. `DvrWorker` scheduler can die silently — `Vora.Infrastructure/Workers/DvrWorker.cs:42-56`**
Only per-session *start* is guarded; the top-level `GetActiveSessionsToStopAsync` / `StopRecordingAsync` / `GetPendingSessionsToStartAsync` calls are unguarded, so a throw reaches the loop where only `OperationCanceledException` is caught and the DVR scheduler stops. *Fix: wrap the whole `ProcessRecordingsAsync` call in try/catch-log inside the `while`.*

## MEDIUM

**6. Transcode contexts leak — no idle reaper — `Vora.Infrastructure/Transcoding/FFmpegTranscodeService.cs:~20,84,234`**
`_transcodeContexts` entries are removed only by the explicit `/stop` endpoint. A client that disconnects/crashes without calling stop leaves the context and its orphaned `.ts` files forever. `TimeshiftCoordinator` has a janitor (`EvictStaleSessionsAsync` + `TimeshiftJanitorWorker`); transcode sessions have none. *Fix: add a hosted reaper that evicts contexts with a stale `LaunchedAt` no longer in `_activeTranscodes` and wipes their temp files.*

**7. DVR recording loop is fire-and-forget with no exception channel — `DvrRecordingService.cs:76,83`**
`_ = Task.Run(() => RecordingLoopAsync(...))` discards the task; any exception thrown *before* the inner `try` (e.g. `FileStream` open on a bad path) is unobserved and the session is stuck in `Recording`. *Fix: move the whole body inside the try, or track the task and mark the session Failed on a faulted continuation.*

**8. `StopRecordingAsync` races the recording loop — `DvrRecordingService.cs:160-172`**
Declared `async` but awaits nothing; it kills `activeRec.Process`, which the loop reassigns each iteration (line ~126) without synchronization — so it can kill a stale/disposed process or miss the live one. *Fix: make it non-async, guard process access, rely on the CTS for loop exit.*

**9. N+1 in discovery enrichment — `Vora.Application/Discovery/DiscoveryManager.cs:139-164`**
`EnrichWithStatusAsync` issues two sequential DB round-trips per item (`MediaExistsByExternalIdAsync` + `GetRequestAsync`) inside the `foreach` — a 40-item page ≈ 80 queries. *Fix: collect all `ExternalId`s, do one bulk exists-lookup and one bulk request-lookup, map in memory.*

**10. Unbounded job dictionaries — `Vora.Application/LibraryMigration/LibraryMigrationJobRunner.cs:21-22,54`**
`_jobs` and `_jobLocks` (singleton `ConcurrentDictionary`) are written on every migration and never removed — they grow for the process lifetime. *Fix: evict completed/failed jobs after a retention window or on terminal-state transition.*

**11. Single shared polling timer stops all libraries — `Vora.Plugins/Providers/Local/PollingFolderWatcherProvider.cs:23,46-55`**
One `_pollingTimer` serves every watched library, so `StopWatching(oneLibraryId)` disposes it and silently stops polling for all others. *Fix: only dispose when `_watchedDirectories` is empty; otherwise just remove that library's entries.*

**12. FolderWatcher TOCTOU race — `Vora.Application/FileSystem/FolderWatcherService.cs:30-56`**
`StartWatching` checks `ContainsKey` then adds much later inside a fired-off `Task.Run`; two concurrent calls for the same library both pass the guard and start two watchers. *Fix: use `TryAdd` as the guard before launching the task.*

**13. Timeshift process started after registration; `TryRegister` result ignored — `Vora.Application/Iptv/IptvManager.cs:319-323`**
`TryRegister` runs before `process.Start()`; if it returns false (duplicate/race) the process is started anyway and orphaned, and the bool is discarded. *Fix: honor the return — kill/skip on false — or start first.*

**14. Transcode FFmpeg stdout redirected but never drained — `FFmpegTranscodeService.cs:435-494`**
`RedirectStandardOutput = true` but the pipe is never read; segments go to disk so it's usually fine, but any stdout write that fills the ~4KB OS buffer deadlocks the encoder. *Fix: set `RedirectStandardOutput = false`, or drain it like stderr.*

**15. Daily-mix orphan cleanup nukes-and-rewrites — `Vora.Application/Media/MusicRecommendationManager.cs:286-294`**
On the first orphaned slot it deletes ALL daily mixes and re-saves `newMixes` (already saved in the loop above), then `break`s — order-dependent, double-saving, and wipes everything if any orphan exists. *Fix: delete only the specific orphaned slots.*

## LOW

**16. FFmpeg argument injection risk — `FFmpegTranscodeService.cs:~704`, `IptvManager.cs:398-421`**
Transcode/timeshift builders interpolate `streamUrl`/`sourceFile`/subtitle paths into a single quoted `Arguments` string; a crafted IPTV URL or filename with a quote could inject options. The DVR and analyzer services already use `ArgumentList` correctly. *Fix: migrate these builders to `ArgumentList`.*

**17. Empty `catch {}` blocks swallow failures** — `DvrRecordingService.cs:139,169`, `TimeshiftCoordinator.cs:153`, `RequestManager.cs:133` (drops `qualityProfileId` override silently). *Fix: at least `LogDebug` in the catch.*

**18. `LogBroadcastHostedService.cs:30` fire-and-forget timer callback** — `_ => FlushAsync()...` is safe only because `FlushAsync` has a bare `catch {}` that also hides real SignalR delivery failures. *Fix: log inside the catch, or use a `PeriodicTimer` loop that awaits.*

**19. Sync file I/O on async paths** — `MusicManager.cs:823` (`File.WriteAllBytes`), `DvrPostProcessingWorker.cs:251` (`new FileInfo(...).Length` with no `File.Exists` guard — throws and faults the session). *Fix: async variants + existence guard.*

**20. `DateTime.Now` for recording filenames — `DvrRecordingService.cs:70`** mixed with `DateTime.UtcNow` for the loop bound; inconsistent/collision-prone in non-UTC containers across DST. *Fix: use `UtcNow` consistently.*

**21. Plugins load into the default (non-collectible) `AssemblyLoadContext` — `PluginLoaderExtensions.cs:88`** so DLLs can't be unloaded (hence the `.deleted` rename workaround) and run at full host trust. *Fix: use a collectible ALC if hot-unload is wanted; otherwise document the constraint.*

## Web client

The player layer is clean. Remaining items are minor:

- **Hardcoded Tailwind palette instead of tokens** (convention violation): `layouts/MainLayout.tsx:359-365` (restricted-profile view — `bg-gray-950`, `text-orange-500`, etc.), `components/Layout/ServerManagerModal.tsx:387` (`bg-white text-black`), `components/LibraryMigration/LibrarySyncPinModal.tsx:120-164` (~6 grays). A handful more `text-white`/`text-orange-*` in `ManageLibrary.tsx`, `DvrDashboard.tsx`, and the music daily-mix views (the orange ones may be intentional brand accent for mixes). *Fix: swap to `var(--vora-*)` tokens.*
- **`contexts/PlayerContext.tsx`**: the `AudioContext` from `ensureAudioGraph` (line ~155) is never `.close()`-d; low impact since the provider is app-root, but add a close on unmount. Two `setTimeout`s (lines ~176, ~260) aren't cleared on unmount — the 260 one could touch refs after unmount. *Fix: cleanup in the effect return.*

Verified NOT issues: `alert/confirm/prompt` hits are all `dialog.*`; `axios` in pages is only `isAxiosError`; `: unknown` is catch-clause typing; modal z-indexes are all `z-[200]`+.

## Suggested order

1. DVR recording service rewrite (#2, #3, #7, #8, #17, #20) — one focused pass fixes the cluster.
2. DVR worker resilience (#4, #5).
3. Music stream auth (#1).
4. Transcode reaper + stdout (#6, #14) and the two unbounded singletons (#10, #11).
5. The rest as cleanup.
