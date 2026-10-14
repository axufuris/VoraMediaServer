# Streaming

How a play request becomes bytes: the path decision, the session, the HLS output and watch state. How subtitles reach the screen is in [`docs/subtitles.md`](subtitles.md); which GPU transcodes, and the CPU retry when it fails, is in [`docs/hardware-acceleration.md`](hardware-acceleration.md).

## The pipeline

```
POST /api/streaming/start        → BestPathDecisionManager picks part + tracks + strategy
                                   StreamManager creates a StreamSession
                                   returns StreamUrl
GET  /api/streaming/play/{id}?t= → DirectPlay: serves the file
                                   otherwise: starts FFmpeg, redirects to the playlist
GET  /api/streaming/hls/s/{token}/{file} → playlist and segments

GET  /api/streaming/sessions/{sessionId}/subtitle/{trackId}.vtt
                                 → text subtitle, from the cache a scan warmed
                                   (extracted on demand if it isn't there yet)
```

`/play` and the HLS route are **not** `RequireAuthorization`. They are gated by short-lived HMAC tokens from `IStreamingTokenSigner`, so a `<video src>` works without carrying an auth header. `/start`, `/start-extra`, and the subtitle route are authenticated normally.

The subtitle route is deliberately **off the video path**. Nothing about starting or serving the stream depends on it, so a slow extraction can't stall or time out playback.

## The decision

`BestPathDecisionManager.DetermineBestPathAsync` scores every (part × audio track) combination against the client's declared capabilities and the server's transcode settings, then takes the lowest penalty. Each option carries a `StreamStrategy` (`DirectPlay` / `Remux` / `Transcode`) plus separate `VideoStrategy`, `AudioStrategy`, and `SubtitleStrategy` strings, because the three can disagree — a session can copy video and transcode audio.

**Which audio track plays, and how it gets there, is in [`docs/track-selection.md`](track-selection.md):** what a client reports about its audio output, what each streaming profile does with it, the scoring weights, why a browser sometimes gets the stream repackaged, and the matrix test that checks every combination. `Remux` is always delivered as HLS and says so in `TargetContainer`, and both `/start` and `/decision` return `audioTrackPosition` for clients that switch tracks themselves.

`OutputResolution` / `OutputHdrType` are what the client actually receives, distinct from the source part's `Resolution` / `HdrType`. A 4K HDR source under `HdrTranscodeDownscale = Always` becomes 1080p SDR, and the player badges, admin Now Playing, and Watch History all read the output values so the UI doesn't claim 4K.

**Scoring compares ladder heights, not raw ones.** A client's reported ceiling is often not a rung the encoder can emit — a phone panel reports 1344, a browser window whatever the user dragged it to. `SnapToDeliverableHeight` reduces it to the rung below (1344 → 1080), and both `effectiveTargetHeight` and `qualityLossPenalty` use that snapped value. Scored raw, an option downscaling 4K to "1344p" looked like a perfect fit while a 1080p part that direct-plays was penalised 2640 for missing a height no file and no encoder can produce — so the server burned a GPU transcode to deliver the 1080p already sitting on disk. The rungs must stay identical to `FormatHeightAsResolution`, or the resolution a decision declares and the one it scored disagree again.

`clientMaxRes` stays **raw** for the capability test (`trackHeight > clientMaxRes` → needs transcode) and its reason string: that one is about what the device can decode and display, and snapping it would let a 1440p source through to a 1344p panel. Each option logs `ClientMaxHeight` / `DeliverableHeight` / `OutputHeight` in `EvaluatedOptions` so a report of this class answers itself.

Subtitle strategies — `None` / `BurnIn` / `DirectPlay`, which track plays when nobody picked one, and why the HLS output carries no subtitles — are in [`docs/subtitles.md`](subtitles.md).

## The transcode directory is flat, namespaced by filename

There is no per-session directory. Everything for a session lands in one directory (`ServerSetting.TranscoderTempDirectory`, default `/transcode`) and is namespaced by a **filename prefix**: the transcode key, which is `session.ExtraId ?? session.MediaItemId` — an extra transcodes under its own id so its stream can't collide with its parent movie's.

```
{key}.m3u8              VOD playlist, written up-front from the segment plan
{key}_{n}.ts            segments: 4 seconds when encoding, cut at the file's keyframes when copying
{key}_{n}.part          a copied segment FFmpeg is still writing (never served)
_ffmpeg_{key}.m3u8      FFmpeg's own internal playlist (encoding)
_ffmpeg_{key}.list      the copied segments FFmpeg has finished, one per line (copying)
```

The **subtitle cache** is deliberately *not* in here — it lives in a `subcache/` subdirectory, is keyed on content rather than session, and is served by its own route (see below).

`GET /api/streaming/hls/s/{token}/{fileName}` serves a file only when **`fileName.StartsWith(<the prefix the token signs>)`** and the extension is one of `.m3u8`, `.ts`, `.m4s`, `.mp4`, `.vtt`. That is the whole authorization model for session output.

Two consequences to keep in mind when adding a new artifact:

- **Name it `{key}…`** or it is unreachable.
- **Don't end the name with `_<integer>`.** `TryParseSegmentRequest` reads `{guid}_{int}` as a segment request and routes it through the seal-and-wait path, so a file named `{key}_1.vtt` would be treated as a segment that never arrives.

The subtitle route — the cache, the FFmpeg call, sidecar files, pre-extraction on scan, Find Subtitles provider plugins, retention and invalidation, client support — is in [`docs/subtitles.md`](subtitles.md).

## Copied video is cut at its own keyframes

When the video is encoded, `-force_key_frames` puts a keyframe every 4 seconds and the HLS muxer cuts there. When it is copied (a Remux, or a session that only converts the audio) a cut can only fall on a keyframe the file already has. The HLS muxer cuts at the first keyframe 4 seconds after the start of **that run**, so after a seek-restart the files no longer matched the playlist: a segment the playlist placed at 148–152 s could hold 140–150 s, and the player buffered the wrong stretch or stalled.

`SegmentPlan` fixes every cut before FFmpeg starts (`PlanSegments` in `FFmpegTranscodeService`):

- **Keyframes come from the file's own index**, not a decode: Matroska Cues found through the SeekHead (`MatroskaKeyframes`), or the MP4 sample tables, `stss` with `stts`/`ctts` and the edit list (`Mp4Keyframes`). `KeyframeIndex.TryRead` returns null for anything else and for a damaged file.
- **A cut is the first keyframe at least 4 s after the previous one** that has no other keyframe within 0.3 s before it or 0.4 s after it, so a small timestamp shift can't move a cut or a restart onto the neighbour. The playlist lists each segment with its own length; `TARGETDURATION` is the longest.
- **FFmpeg runs the segment muxer** (`-f segment -segment_times …`), which cuts exactly where it is told. Each cut time sits 0.15 s before its keyframe, because FFmpeg's timeline is the file's minus its start time (AAC priming, B-frame delay), and a cut placed exactly on the keyframe could miss it and cut at the next one.
- **A restart at segment n** seeks to 0.25 s past its keyframe with `-noaccurate_seek` (FFmpeg backs a seek off by 3/23 s when the video has B-frames, which still lands on the keyframe) and sets `-output_ts_offset` to the seek plus 1 s. Every run writes the same segments with the same timestamps, whatever segment it started from.
- **Even 4-second segments stay** for encoded video, an unknown duration, AVI / TS / other containers, a fragmented MP4, a first keyframe after 0.5 s, and keyframes too dense to cut between (median gap under 0.4 s). The log says which plan a session got.

**Only finished segments are served.** The HLS muxer wrote each segment under a temporary name and renamed it; the segment muxer writes in place. So a copied segment is written as `{key}_{n}.part`, FFmpeg lists it in `_ffmpeg_{key}.list` once it is closed, and `PublishFinishedSegments` renames listed ones to `.ts`, replacing an earlier run's copy in one step. A copy session counts a segment as ready only when its `.ts` exists. A relaunch first publishes what the killed run finished, then deletes the list and the `.part` files left behind.

**MPEG-TS, not fMP4.** With fMP4 the timestamp offset lands in the edit list rather than the fragments, and with `-copyts` the first HEVC fragment after a restart came out 0.126 s late. TS restarts are exact, and hls.js plays HEVC from TS.

The seek-restart rules count **seconds, not segments**, because copied segments vary in length: a request within 4 minutes of where the current run started waits for it, and a request more than 20 s behind it is a real seek once something has been served.

Tests: `SegmentPlanTests`, `KeyframeReaderTests` (built EBML and MP4 boxes), `KeyframeSegmentArgumentTests` (pinned FFmpeg arguments), `SegmentPublishingTests`, and `KeyframeSegmentFileTests`, which runs real FFmpeg on H.264 and HEVC in MKV and MP4 with uneven keyframes: the index must match ffprobe, every segment must start on its planned keyframe, and two restarts must write the same segments as a whole run. It is skipped without ffmpeg and ffprobe on PATH.

## Watch state

A progress ping writes `UserMediaState` through `StreamRepository.UpdateUserMediaStateAsync`, and `WatchStateTransition` decides what it means:

- past **90%** → played, resume cleared to 0
- a re-watch that gets **15 seconds** in → **not** played any more, resume where the viewer is
- anything else → left as it was

`IsPlayed` used to be `IsPlayed || completed`, so it could only ever go true. Re-watching a finished item stored a resume position nothing surfaced, because all three consumers require it to be false: the details page (`inProgress = resumePos > 0 && !isPlayed`), the watched check, and the Continue Watching query (`ResumePositionSeconds > 0 && !s.IsPlayed`). The only escape was marking it unwatched by hand.

The 15-second window exists because a ping can land at position 0 before the player has seeked to its resume point — clearing on that would wipe a finished item's state for someone who never restarted it. An unknown duration (Live TV) leaves the flag alone entirely.

## Watch history

Admin → Watch History (`GET /api/streaming/admin/history`, `StreamHistoryProjection`) lists stream sessions grouped by profile, device and title for each **local day** in the server time zone (`ScheduleClock`, `AT TIME ZONE` in SQL). Grouping by UTC day used to fold an evening's watch into the next morning's sessions for anyone west of Greenwich, and gave the row the newest session's date with the oldest session's time.

- **Everything filters on the server, then pages.** `profileId`, `libraryIds` and `streams` (`DirectPlay`, `DirectStream`, `Transcode`; comma-separated) narrow the sessions before grouping. Groups are ordered by their most recent session, with ties broken by item, profile and device so a row can't move between pages, and `total` counts groups. The page used to load page 1 only and filter those rows in the browser.
- **Profile, then account.** A row names the profile that watched and, when the two differ, the account beneath it. A profile keeps the name it was created with when the account is renamed. `profileId` filters on the server; the dropdown lists every profile from `GET /api/users`, grouped by account.
- **Title.** An episode row leads with the show; the episode number and title sit beneath it (`S02E01 · Ooze`).
- **IP and location** are the stream's own: `StreamSession.IpAddress` / `Location`, written when the stream starts. `StreamingEndpoints` passes `HttpContext.GetClientAddress()` to `StreamManager`, because the device row is only refreshed every five minutes and can still hold the previous network. The location is the device's when the address matches the device's last one, and otherwise comes from `IClientLocationResolver` (the same lookup `DeviceTrackingMiddleware` uses; a public address is remembered for 12 hours). The same address decides whether the stream counts as remote for the bandwidth limits. A session started before these columns existed has neither, and shows the device's current address and location (`ClientDevice.LastIpAddress` / `Location`) as it did before; a session with an address but no location shows no location, not the device's. Now Playing reads the same fields.
- **Duration** is time played: elapsed time minus paused time (`WatchProgress.WatchedSeconds`).
- **Status** (`WatchProgress.PercentComplete`) trusts where a session stopped only when at least half of the stretch it moved across was actually played; otherwise only the minutes played count, from where it started. Skipping an intro or the credits still finishes a title, while jumping to the end in thirty seconds doesn't. A grouped row shows the furthest any of its sessions got. The length is the played part's (`MediaPartId`), falling back to the item's analysis.

## Gotchas

- **Concurrent sessions on the same title share a transcode.** `_activeTranscodes` is keyed by media item id, so two clients playing the same movie collide on the video. Subtitles are not affected — the cache is keyed on (part, track), so two viewers with different subtitle choices get different files.
- **The subtitle route needs the auth header**, so a client can't point a `<track src>` or a native player's URL loader straight at it. Fetch it through the normal API client and hand the player the bytes (on web, a blob URL).
- **A first request can take as long as the extraction.** That is the trade for never blocking `/start`; it costs once per (part, track), and the client owns the request so it can show its own pending state.
- **Token scope and temp-dir defaults live on `StreamManager`** (`PlayTokenScope`, `HlsTokenScope`, `HlsTokenTtl`, `DefaultTranscodeTempDirectory`) and `StreamingEndpoints` references them. They were duplicated literals; a silent drift 404s every subtitle.
