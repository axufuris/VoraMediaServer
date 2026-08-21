# Video preview thumbnails

Scrub-bar preview thumbnails for the video player, analogous to Plex's hover thumbnails. One JPEG sprite sheet plus a WebVTT cue file per media item. Generation runs as a daily background pass; only libraries with the "Enable video preview thumbnails" checkbox turned on are processed, and only items in video-bearing libraries (Movies, TV, HomeVideos).

## Storage

Configured via `StoragePaths:VideoThumbnails` (env var `StoragePaths__VideoThumbnails`). Defaults to `<AppContext.BaseDirectory>/video-thumbnails`; in Docker it lives at `/app/data/video-thumbnails` under the existing `./Vora-data:/app/data` volume so the cache survives container rebuilds. **No new Docker volume is required.**

Per-item directory, sharded by the first two hex chars of the GUID, with **one sprite set per cut** stored under the owning ("source") part's id:

```
<root>/<shard>/<mediaItemId>/
  <sourcePartId>/
    sprite.webp       # tiled WebP: COLS × ROWS tiles at WxH each
    thumbnails.vtt    # WebVTT cues, each referencing sprite.webp#xywh=x,y,w,h
  .version            # short hash of the settings used at generation time
```

Thumbnails are **per media part**. At generation the item's parts are grouped into *cuts* by runtime (±5s): parts in the same cut share one sprite (generated once from the group's representative), and a part whose runtime differs by more than the tolerance — a genuinely different edit — gets its own. Each `MediaPart` records `ThumbnailSourcePartId` (the part that owns its sprite files, itself or a same-runtime sibling) plus the sprite metadata copied from that source. So a 1080p + 4K of the same cut store **one** sprite referenced by both, while a theatrical vs extended pair store two. The `.version` file is a hash of `(interval, width, height, jpegQuality, columns)`; the manager compares it against the item-level `MediaItem.VideoThumbnailSpriteVersion` (still the coverage/target aggregate) to decide whether the artifacts are current.

The per-part layout is **backward compatible and not force-migrated**: the version seed is deliberately left at `v2-webp` so an existing library isn't marked stale. Items generated before the change keep their item-level sprite (served via the legacy fallback in the endpoints); the per-part layout is produced only for items generated from now on and whenever a part is added (`AddMediaPartAsync` nulls the item's version). To move an existing multi-part item onto per-part sprites deliberately, use the per-item or per-library **Regenerate**. Changing the seed would mark every item stale and re-encode the whole library just to relocate identical single-part sprites — don't, unless the sprite bytes themselves must change (size/interval/quality already vary the hash on their own).

## Server-wide settings

All in `ServerSetting` (admin UI: System Settings → Video Preview Thumbnails):

- `VideoThumbnailScheduleTime` — `TimeSpan`, default `04:00`. Daily run time. Mirrors the `DetectionScheduleTime` pattern.
- `VideoThumbnailIntervalSeconds` — seconds between captured frames, default `10`.
- `VideoThumbnailWidth` / `VideoThumbnailHeight` — tile size, default `320 × 180`.
- `VideoThumbnailJpegQuality` — FFmpeg's `-qscale:v` (2–31, lower = better), default `5`.
- `VideoThumbnailSpriteColumns` — tiles per row in the sprite, default `10`.
- `VideoThumbnailConcurrency` — how many items the sprite pass decodes at once, default `2` (clamped 1–16).
- `VideoThumbnailUseHardwareDecode` — default `true`. Whether the sprite pass decodes on the GPU. It only takes effect when the global `UseHardwareAcceleration` (Transcoding tab) is on, so the FFmpeg command uses `-hwaccel` only when **both** are true. Turn it off to keep thumbnail generation on the CPU and leave the GPU for playback transcoding or other apps (e.g. Tdarr) — it does **not** bump the sprite-version hash, so toggling it never triggers a regeneration.

Changing any of the geometry/quality values above bumps the sprite-version hash, so the next scheduled pass regenerates affected items (locked items still skip). Concurrency and the hardware-decode toggle do not.

## Per-library opt-in

`MediaLibrary.EnableVideoPreviewThumbnails` (existing flag). The UI checkbox is hidden for Music, LiveTv, and any non-video MediaType; `LibraryManager.CreateLibraryAsync` and `UpdateLibraryAsync` also coerce the value to `false` server-side for those types, so a stale client can't enable it.

When the flag flips `true → false`, `LibraryManager.UpdateLibraryAsync` calls `IVideoThumbnailManager.PurgeLibraryThumbnailsAsync` to wipe stored sprites for that library.

## Per-item state

On `MediaItem`:

- `LastVideoThumbnailGenerationAt` — UTC timestamp of the most recent successful run.
- `VideoThumbnailSpriteVersion` — the hash used when this item was generated.
- `VideoThumbnailSpriteCount`, `VideoThumbnailIntervalSeconds`, `VideoThumbnailSpriteColumns`, `VideoThumbnailWidth`, `VideoThumbnailHeight` — what was actually produced (the API doesn't currently surface these, but they're available for future scrub-bar density tuning).

## Lock semantics

`LockedFields["Thumbnails"]` follows the same `LockableEntity` pattern as `LockedFields["Markers"]`. When present, both the scheduled pass and admin "Regenerate thumbnails" skip the item until the lock is removed. Exposed via:

- `IMediaRepository.AreThumbnailsLockedAsync(Guid)` / `SetThumbnailsLockedAsync(Guid, bool)`
- `GET / PUT /api/media/{id}/thumbnails/lock` (admin only)

## Generation pipeline

`FFmpegVideoThumbnailGeneratorService` runs a single FFmpeg invocation per video:

```
ffmpeg -y [-hwaccel auto [-hwaccel_device <dev>]] -skip_frame nokey -i <input>
  -vf "fps=1/<interval>,scale=W:H:force_original_aspect_ratio=decrease:flags=fast_bilinear,
       pad=W:H:(ow-iw)/2:(oh-ih)/2:color=black,
       tile=COLSxROWS"
  -frames:v 1 -an -sn
  -c:v libwebp -quality <0-100> -preset picture -f image2 <out>
```

`tile` flattens the captured frames into a single sprite. The scale + pad chain ensures every tile is exactly W × H even when the source aspect ratio differs (letterboxed with black). Frame count is `ceil(duration / interval)`; row count is `ceil(count / columns)`. Source duration comes from `ffprobe -show_entries format=duration`.

- **`-skip_frame nokey`** (an input/decoder option, so it precedes `-i`) makes the decoder emit keyframes only — the `fps` filter still yields one tile per interval from the nearest keyframe, so the grid and WebVTT cues are unchanged, but the dominant cost of fully decoding B/P frames disappears.
- **`-hwaccel auto`** is added only when the global `UseHardwareAcceleration` **and** the per-feature `VideoThumbnailUseHardwareDecode` toggle are both on; if a GPU decode pass fails on a codec/profile NVDEC can't handle, the service retries the same command in software so one quirky file never blocks the library.
- **`libwebp`** output is ~25–35% smaller than JPEG at equal quality. The stored quality is an FFmpeg qscale (2 = best … 31 = worst) mapped to libwebp's 0–100 scale, so the one admin setting keeps its meaning. `-f image2` pins the muxer because the temp file uses a `.webp.tmp` suffix during atomic write and FFmpeg's extension autodetect chokes on `.tmp`. The sprite-version seed is `v2-webp|…`, so flipping to WebP invalidated old JPEG sprites automatically; a stale `sprite.jpg` left by an older generation is deleted after a successful WebP run.

### Failure handling

A per-item ffmpeg failure never aborts the batch — `GenerateManyAsync` runs items in parallel scopes and `RunSingleAsync` catches the exception, logs it, and moves on (cancellation is rethrown, not swallowed). Two things make a broken source visible instead of silent:

- On generation failure (commonly a **corrupt/unreadable file** — e.g. ffmpeg misdetecting an MKV as raw audio, so there's no video stream) the item is skipped, an `Error` is logged with the file path, and a **deduplicated** `AdminNotification` (severity `Warning`, title "Video preview thumbnails failed") is raised naming the file to fix. Dedupe is keyed on the item id (`{"thumbnailFailure":"<id>"}`) so a permanently-broken file surfaces **one** actionable alert rather than a fresh one on every scheduled pass until the admin clears it.
- The "no media file path on record" early-return, previously silent, now logs a `Warning` — so an item that shows as missing coverage with no error is explained.

A failed item keeps no version marker, so it stays in the target list and is retried on the next pass (once the file is fixed, it succeeds and the alert stops).

After the sprite lands on disk, the service synthesizes a WebVTT file:

```
WEBVTT

00:00:00.000 --> 00:00:10.000
sprite.jpg#xywh=0,0,320,180

00:00:10.000 --> 00:00:20.000
sprite.jpg#xywh=320,0,320,180

...
```

Both files are written to `<final>.tmp` first, then atomically renamed.

## Scheduling

`ScheduledJobWorker` checks the schedule on its existing 5-minute timer. When `timeOfDay >= VideoThumbnailScheduleTime` and we haven't run today, it queries `ILibraryRepository.GetAllProjectedAsync` for libraries that have the checkbox on **and** a video-bearing `Type`, and enqueues `ITaskQueueManager.QueueGenerateLibraryVideoThumbnails` for each.

Thumbnails are **never** queued on ingestion — they're background-only by design (matches the original spec). DVR recordings are explicitly out of scope.

## API surface

Authenticated client endpoints (any logged-in profile with access to the library):

- `GET /api/media/{id}/thumbnails.vtt?partId={partId}` — serves the cue file for the requested part's cut (resolves `ThumbnailSourcePartId`); omit `partId` to get a stable representative. ETag based on file mtime, long Cache-Control. Falls back to the legacy item-level file for anything generated before the per-part layout.
- `GET /api/media/{id}/thumbnails.jpg?partId={partId}` — serves the sprite, same shape. The player derives `partId` from the video track being streamed, so scrubbing the 4K vs 1080p (or an alternate cut) shows the sprite for the file actually playing.

Admin endpoints (`AdminOnly`):

- `POST /api/media/{id}/thumbnails/regenerate` — queues a single-item regen (forces override). Looks up `MediaItem.Title` first so the Background Tasks list shows `Generate Video Thumbnails: <item title>` instead of a raw GUID.
- `POST /api/libraries/{id}/thumbnails/regenerate` — queues a library-wide regen. Same name-lookup treatment via `ILibraryRepository.GetProjectedByIdAsync(id, l => l.Name)`.
- `GET /api/libraries/{id}/thumbnails/coverage` — returns `{ total, withThumbnails }` filtered to Movies + Episodes only.
- `GET / PUT /api/media/{id}/thumbnails/lock` — read/toggle the `LockedFields["Thumbnails"]` flag.

## Player integration

`GlobalVideoPlayer` calls `useVideoThumbnails(mediaItemId, serverId)` from `components/Player/VideoScrubThumbnails.tsx`. The hook:

1. Fetches the VTT with the Authorization header.
2. Parses cues into `{ start, end, x, y, w, h }`.
3. Fetches the sprite as a Blob, creates an object URL for it (`<img>` and CSS `background-image` don't send Authorization headers, so a static URL wouldn't work).
4. Returns `{ available, width, height, spriteUrl, findCue }`. Binary search on `findCue`.
5. Subscribes to the `VideoThumbnailsReady` SignalR event — if the currently-playing item finishes generation, the hook re-fetches without a refresh.

`<ScrubThumbnail>` renders a `position: fixed`, pointer-events-none tile above the progress bar at `barRect.top - height - 14`. Background-position uses the cue's `x,y` to expose the right slice of the sprite. Hover/leave handlers on both the maximized and minimized scrub bars feed it `hoverPercent` and the bar's `getBoundingClientRect`.

Live TV and DVR contexts are excluded from the hook (`playbackContextType !== 'LiveTv' && !== 'Dvr'`).

## Admin UI

- **System Settings → Video Preview Thumbnails** — schedule time, interval, width, height, JPEG quality, sprite columns.
- **Manage Library** — `ThumbnailCoverageCard` shows `total / withThumbnails / missing`, plus a "Regenerate missing" button that calls the library admin endpoint, pops a confirmation dialog, and auto-refreshes the coverage stats so the user can watch the missing count tick down. The card hides itself for non-video library types.
- **Media details page (admin menu)** — "Regenerate thumbnails" and "Lock/Unlock thumbnails" appear in the dropdown next to "Analyze media" for Movie and Episode items.

## SignalR

`IClientNotifier.NotifyVideoThumbnailsReadyAsync(Guid mediaItemId)` → SignalR event `VideoThumbnailsReady` carrying the media item id. Fired at the end of `VideoThumbnailManager.RunSingleAsync` after the DB row is updated.

## What's intentionally out of scope

- **DVR recordings** — they're finished MP4-ish files but don't sit in a `MediaLibrary` with the checkbox. Adding them later would require a separate server-level toggle.
- **Per-library overrides** for interval/dimensions — settings are server-wide for now. Add `MediaLibrary` columns if denser intervals on movies vs. home videos become useful.
- **On-ingestion auto-queue** — the original spec said background-only. Hook into `MediaIngestionService` later if "new uploads should have thumbnails within minutes" becomes a requirement.
