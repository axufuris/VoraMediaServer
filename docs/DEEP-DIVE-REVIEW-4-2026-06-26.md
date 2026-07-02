# Vora Deep-Dive Review #4 — 2026-06-26

Fourth pass, on ground the first three didn't cover: **concurrency/race conditions**, **domain business-logic correctness**, the **Android client**, and **data-integrity/transactions/serialization**. Higher-severity items were hand-verified.

## HIGH

**1. EPG parental-control filtering corrupts the shared cache for all users — `IptvEpgService.cs:340-352` (CONFIRMED).**
`ApplyParentalControls` mutates `program.Title`/`program.Description` in place, but `GetProgramsForChannels` (line 91-102) returns lists of the **same `IptvProgramDto` instances** held in the static `_memoryCache`. The moment any restricted profile opens the guide, the matching programs are permanently relabeled "Restricted Content" in memory for **every** user/profile until the next EPG sync. *Fix: build new DTOs (or deep-copy) in `GetFilteredGuideAsync` before applying parental controls; never mutate cached instances.*

**2. FileSystemWatcher async-void event handlers can crash the process — `NativeFolderWatcherProvider.cs:37-39` (CONFIRMED).**
`watcher.Created += async (s, e) => await onFileAdded(...)` (and Renamed/Deleted) compile to `async void`. An exception from `onFileAdded`/`onFileDeleted` (DB error, parse failure, bad path) escapes to the thread pool unobserved and can take down the process — there's no try/catch anywhere in that path. *Fix: wrap each handler body in try/catch-log; you can't await across an event boundary so the exception must be caught there.*

## MEDIUM

**3. EPG unrated check is wrong for null/empty ratings — `IptvEpgService.cs:346-347` (CONFIRMED).**
Unrated programs are detected by `ContentRating == "NR"`, but the common "unknown" value is `null`/empty, not the literal `"NR"`. A program with no rating is treated as restricted even when `BlockUnratedContent` is false, and `allowedRatings.Contains(null)` is a quiet no-match. *Fix: treat null/empty/"NR" uniformly as unrated and gate them all on `blockUnratedContent`.*

**4. Tuner-allocation TOCTOU — over-subscribes tuners — `DvrManager.cs:73-99` + `IptvManager.EnsureTunerAvailableAsync:348` (CONFIRMED).**
`CanAllocateTunerAsync` counts active recordings under `_tunerLock`, releases, then the session row is written later outside the lock; the timeshift path (`EnsureTunerAvailableAsync`) does the same check with **no lock at all**. Two near-simultaneous starts (or a DVR start racing a timeshift) both pass the `MaxConcurrentStreams` gate. *Fix: route DVR + timeshift tuner gating through one critical section that spans the count-check and the status write, or enforce with a DB-side conditional update.*

**5. Email-change apply is two non-atomic saves — `AuthManager.cs:522-526, 600-604` (CONFIRMED; this session's code).**
`ConfirmEmailChangeAsync`/`ChangeEmailAsync` update the user (save) then invalidate the ticket (separate save). If the second fails, the email is changed but the consumed ticket stays active/reusable. *Fix: delete/invalidate the ticket in the same `SaveChanges` as the user update, or wrap both in one transaction.* (Data integrity is otherwise safe — `User.Email` has a unique index that backstops the uniqueness race.)

**6. Unbounded admin paging — `StreamHistoryProjection.cs:38` / `StreamManager.GetGroupedHistoryAsync` and `AiStatsManager.GetDashboardAsync` (CONFIRMED).**
`Skip((page-1)*pageSize).Take(pageSize)` with no upper clamp on `pageSize`; a huge value is an OOM/expensive-query vector and `(page-1)*pageSize` can int-overflow to a negative `Skip` (throws). `MusicManager.GetAdminMusicHistoryAsync` already clamps correctly — mirror it. *Fix: `Math.Clamp(pageSize, 1, 200)` and `page >= 1` in those managers.*

**7. Daily-mix orphan cleanup nukes blended state — `MusicRecommendationManager.cs:286-294` (CONFIRMED; also raised in review #1, still open).**
When the cluster count drops (e.g. 4→3), the orphan branch deletes ALL daily mixes and re-saves the un-blended fresh order, discarding the drift-blended `TrackOrder` the update loop just computed. The `foreach…break` wrapper is a confusing no-op. `GenerateMoodMixesAsync` (line ~1204) has the sibling bug: stale mood slots are never deleted. *Fix: delete only the truly-orphaned slots; keep the blended results.*

**8. `DeviceTrackingMiddleware` leaks a `SemaphoreSlim` per device id — `DeviceTrackingMiddleware.cs:22,94` (CONFIRMED, slow leak).**
The `static ConcurrentDictionary<string, SemaphoreSlim>` grows one entry per distinct `X-Vora-Device-Id` forever (never removed/disposed); spoofed/ephemeral ids leak memory + handles unboundedly. Release is correctly in `finally`. *Fix: evict the per-device semaphore after use or tie it to the IMemoryCache entry lifetime.*

**9. Android: silent playback hang on controller-connect failure — `AudioPlayer.kt:91-101,228 (CONFIRMED).**
If the `MediaController` connect Future faults, `runCatching` swallows it, leaving `pendingPlay` set, `mediaController` null, and state stuck on `Empty` — playback hangs with no error. *Fix: on connect failure reset `controllerFuture = null` and emit an error/empty state.*

**10. Android: token-mint failure leaves a half-broken player screen — `AudioPlaybackScreen.kt:95-112`, `AudioBrowseTree.resolveForPlayback` (CONFIRMED; this session's token change).**
If `resolveStreamUrl` fails, the screen sets `error` but still renders tappable transport controls over an `Empty` player; Android Auto/Wear (`resolveForPlayback`) falls back to the now-401 `/stream` URI and fails with a generic error. (LiveRadio/Podcast screens have no error state at all.) *Fix: gate controls on `error == null` / add retry; for the Auto path return null so the session reports a real error.*

## LOW

**11. Marker heuristics over-trigger** (`MarkerAssembler.cs`): Recap emitted for any two-gap intro (line 126-146); credits-roll detected at the first joint gap past 60% — a mid-film silent scene mislabels the rest of the movie as credits (line 148-156); `ClusterMedian` uses the positional median rather than the densest cluster, so a bimodal season can fail agreement (`MediaAnalyzerManager.cs:359`). All are tuning/edge-case issues, not crashes.

**12. DVR single-recording match gaps — `DvrManager.cs:253-268`.** Single (non-series) match has no future-time guard (series does) and a title-only single recording with `ProgramId == null` never matches. *Fix: add `StartTime > UtcNow` and support title match.*

**13. `CompleteSessionAsync` 5 MB success floor — `DvrRecordingService.cs:277`.** A legitimately short/low-bitrate recording is flagged Failed (and may be retention-deleted). *Fix: base success on elapsed-vs-expected duration.*

**14. Library-migration merge nits — `LibraryMigrationJobRunner.cs`.** `MergeWatchStates` can report `IsPlayed=true` with a non-zero resume position (contradictory; zero the resume when played wins); `Skipped` double-counts items unmatched in both passes (line 250).

**15. Smart-playlist `IsNull`/`IsNotNull` ignores the coalesced fallback — `SmartPlaylistEvaluator.cs:426`.** For Artist (member `Artist`, fallback `ArtistName`), `IsNull` checks only `Artist`, contradicting every other operator on that field.

**16. `CollectionOrderingService.ReevaluateOrderOnItemAddedAsync:57` only re-sorts when `forceFullRefetch && providerId != null`** — a normal add to a Chronological collection leaves the new item at its default sort order until a manual refetch.

**17. Android minor**: `PodcastEpisodePlaybackScreen.kt:106` `while(true)` save loop (others use `while(isActive)`); crossfade poll runs 4×/sec even when crossfade is disabled (`VoraAudioPlaybackService.kt:126`); every player builds its own `OkHttpClient()` (no shared instance — allocation churn, not a leak); `HomeHero.kt:73` `items.first()` fallback could throw on an empty spotlight (confirm the caller guards).

**18. Latent: int-backed enum columns** (`GeneratedMix.Kind`, `Station.SeedKind`, `Playlist.MediaType`, `SmartPlaylist.MediaType` — `.HasConversion<int>()`). Append-only today so safe, but inserting an enum value mid-list would silently remap persisted rows. *Consider string conversion (needs a migration), or treat these enums as append-only by convention.*

## Confirmed clean (verified)
Email-change uniqueness is backstopped by a unique index on lowercased `User.Email`; backup restore is properly atomic via one `IBackupTransaction`; marker replacement and collection sync are transactional/single-save; cascade-delete behavior is sensible (Track→Album is correctly SetNull); unique constraints are well-covered (devices, tickets, per-profile ratings, mix slots, IPTV channels); `Task.WhenAll` sites fan out over HTTP, not a shared DbContext; no `lock` held across `await`; all `SemaphoreSlim.Release()` in `finally`; the `DvrRecordingService` rewrite and `TranscodeJanitorWorker` from earlier this session are sound. **Android**: ExoPlayer lifecycle (build in `remember`, release in `DisposableEffect`), `applicationScope` for dispose-time work, no `GlobalScope`/`!!`/deprecated-API/unused-imports, equalizer release+rebind, the music-stream token resolution throws (not swallowed) on the critical path.

## NOT a finding (sandbox artifact)
`VoraDbContextModelSnapshot.cs` reads as truncated mid-statement (`b.Navigation("Mark`) in the review sandbox. This is a **mount/read artifact**, not a real defect — the project builds and tests pass, so the actual file on disk is intact. (This mount has had file-read corruption before; no action needed beyond a sanity glance at the file in the IDE.)

## Suggested order
1. **EPG cache corruption (#1)** — affects all users, easy fix.
2. **Watcher async-void (#2)** and **email-change atomicity (#5)** — small, robustness.
3. **Tuner TOCTOU (#4)**, **unbounded paging (#6)**, **daily-mix cleanup (#7)**, **device-semaphore leak (#8)**.
4. Android #9/#10, then the LOW tuning/edge items.

## Closeout — fixes applied 2026-06-26

All HIGH + MEDIUM items (#1–#10) fixed. LOW items (#11–#18) not yet actioned.

- **#1 / #3 EPG** — `ApplyParentalControls` is now non-mutating: it returns a new `Dictionary<string,List<IptvProgramDto>>`, builds fresh DTOs only for restricted programs, and reuses cached refs for allowed ones, so the shared `_memoryCache` is never relabeled. Unrated detection now treats null/empty/"NR" uniformly via `IsProgramAllowed`. Helper tests updated to assert on the return value (plus a "cached instance untouched" assertion).
- **#2 Watcher** — `NativeFolderWatcherProvider` now injects `ILogger` and routes every `Created`/`Renamed`/`Deleted` handler through `SafeInvokeAsync`, a try/catch-log wrapper, so a handler exception can no longer escape `async void` and crash the process.
- **#5 Email-change** — new `IUserRepository.ApplyEmailChangeAsync` updates the user and removes outstanding `EmailChangeTicket`s in a **single `SaveChanges`**; both `ChangeEmailAsync` (admin/direct branch) and `ConfirmEmailChangeAsync` call it.
- **#4 Tuner TOCTOU** — new singleton `ITunerGate` (per-playlist `SemaphoreSlim`). `DvrWorker` now runs the count-check **and** `StartRecordingAsync` (which flips the row to `Recording`) inside one `RunExclusiveAsync` critical section, closing the DVR-vs-DVR over-subscription window. The old global `_tunerLock` in `DvrManager` was removed. `IptvManager.StartTimeshiftSessionAsync` routes its tuner check through the same gate. **Residual (pre-existing, not introduced here):** timeshift sessions are not counted by `GetActiveRecordingCountForPlaylistAsync` at all, so two concurrent *timeshift* starts still aren't bounded by the tuner limit — making timeshift consume a counted tuner slot is a separate design change; flagged for a decision.
- **#6 Paging** — `StreamManager.GetGroupedHistoryAsync` now clamps `page >= 1`, `pageSize ∈ [1,200]`; `AiStatsManager.GetDashboardAsync` adds an upper clamp (`MaxPageSize = 200`).
- **#7 Daily-mix** — orphan branch now calls new `DeleteMixSlotsAsync(profileId, kind, slots)` to delete **only** orphaned slots, preserving the drift-blended `TrackOrder`. `GenerateMoodMixesAsync` now tracks produced slots and deletes stale mood slots after the loop.
- **#8 Device-semaphore** — the unbounded `static ConcurrentDictionary` is gone; per-device locks now live in `IMemoryCache` with a 5-min sliding expiration, so unused locks are evicted and GC'd.
- **#9 Android player** — `AudioPlayer.ensureController` adds `.onFailure { … }`: on a controller-bind fault it resets `mediaController`/`controllerFuture`/`pendingPlay` and emits a new `AudioPlayerState.Error` instead of hanging on `Empty`.
- **#10 Android screen / Auto** — `AudioPlaybackScreen` unifies the resolve error and the player `Error` state into `effectiveError`, gates the transport controls behind it, and shows a Retry button. `AudioBrowseTree.resolveForPlayback` now returns `null` (not the stale 401 URI) when token-mint fails, so the media session reports a real error.

Build/test: backend compiles in VS (API in Docker); `dotnet test` for the updated EPG + IPTV-manager tests. Android builds with `-Werror`.

## Follow-up — unified tuner budget (#4 full fix), 2026-06-26

The original #4 fix closed the DVR-vs-DVR race but left a bigger gap: **live passthrough viewing and timeshift were never counted** against `MaxConcurrentStreams`, so the limit only ever governed DVR. A household watching live channels could silently blow past the provider's connection cap. This follow-up makes all three persistent provider pulls share one budget. **Policy: no preemption** — a DVR recording that can't get a free tuner is marked `Conflict` (existing behavior); active live viewers are never interrupted.

- **`ITunerRegistry` / `TunerRegistry`** (new singleton, `Vora.Application/Iptv`) — the single in-memory budget. `TryAcquire(playlistId, maxConcurrent, leaseKey, kind)` atomically counts current leases for the playlist and admits or refuses; `Heartbeat`, `Release`, `ActiveCount`, `EvictIdle(kind, maxIdle)`. `maxConcurrent <= 0` = unlimited (unchanged semantics). Replaces the `ITunerGate` introduced in the first #4 pass (now removed — `ITunerGate.cs` is blanked, delete it in VS).
- **DVR** — `DvrRecordingService.StartRecordingAsync` now returns `bool`, acquires a `dvr:{sessionId}` lease right before spawning ffmpeg (marks `Conflict` + returns false if the budget is full), and releases in the recording loop's `finally`. `DvrWorker` just calls it. `DvrManager.CanAllocateTunerAsync` + its 4 DB-count tests were removed (superseded).
- **Timeshift** — `IptvManager.StartTimeshiftSessionAsync` acquires a `timeshift:{profileId}` lease (returns null → 409 when full); `StopTimeshiftSessionAsync` releases it. `TimeshiftCoordinator.EvictStaleSessionsAsync` now returns the evicted profile ids so the janitor releases their leases. Lease lifecycle is separated from process lifecycle (the internal "stop prior session" call no longer touches the lease).
- **Live passthrough** — `StartPassthroughAsync` acquires a `live:{guid}` lease for HLS channels (audio/radio is not counted) and embeds the lease id in the playlist token (`leaseId|url`). The lease id is threaded through playlist rewriting so child/variant playlist tokens carry it too; each playlist poll (`GetRewrittenPlaylistAsync`) heartbeats the lease. `TunerLimitReachedException` → **409** at the endpoint. Idle live leases are reclaimed by `TimeshiftJanitorWorker` via `EvictIdle(Live, 90s)`.
- **Clients** — web `LiveTvPlayer` shows an "all tuners in use" dialog on a 409. **Android now mirrors the web** (built 2026-06-26, superseding the earlier "keep direct-play" call): Android live TV used to play the channel's *raw upstream URL* directly, which both bypassed the budget and broke channels that need Vora's proxying (e.g. a `.mp4` promo source forced into an HLS mime → `MANIFEST_MALFORMED`). It now resolves every channel through Vora — `timeshift/start` when the profile has `CanTimeshiftIptv`, else `passthrough/start` — plays the proxied HLS, pings every 30s during a timeshift session, stops it on dispose, and surfaces a 409 as an "all tuners in use" message. So Android live viewers now count in the budget too. *Requires an OpenAPI client regen* (new typed VMs below).
- **Typed start VMs** — the passthrough/timeshift `start` endpoints returned anonymous objects, so the generated Kotlin client couldn't read `url`/`streamType`. Added `PassthroughStartVM {url, streamType}` and `TimeshiftStartVM {url}` (in `Vora.Application/Iptv/ViewModels`) and `.Produces<>` on both endpoints. **Rebuild the API, then `./gradlew :core:refreshOpenApi` + regenerate** before building Android, or the new `IPTVPassthroughApi`/`TimeshiftApi` calls won't resolve.
- **Known minor**: a live viewer who stalls >90s (lease evicted) and then resumes on the *same* playlist token plays untracked until they restart; in practice a >90s gap triggers a fresh start. The DVR scheduling `DropNewest` look-ahead still uses the DB recording count (it plans future overlaps, not live admission) — intentionally left DB-based.
- **Tests**: new `TunerRegistryTests` (limit/whole-budget/release/unlimited/idempotent-key/per-playlist/idle-evict/heartbeat). `IptvManagerTests` now uses a real `TunerRegistry`.
