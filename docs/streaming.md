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

**Which audio track plays, and how it gets there, is in [`docs/track-selection.md`](track-selection.md):** what a client reports about its audio output, what each streaming profile does with it, the scoring weights, why a browser sometimes gets the stream repackaged, and the matrix test that checks every combination. `Remux` is always delivered as HLS and says so in `TargetContainer`, and both `/start` and `/decision` return `audioTrackPosition` for clients that switch tracks themselves, and `subtitleTrackPosition` for the subtitle (an image subtitle a client draws itself is in [`docs/subtitles.md`](subtitles.md)).

`OutputResolution` / `OutputHdrType` are what the client actually receives, distinct from the source part's `Resolution` / `HdrType`. A 4K HDR source under `HdrTranscodeDownscale = Always` becomes 1080p SDR, and the player badges, admin Now Playing, and Watch History all read the output values so the UI doesn't claim 4K.

**Scoring compares ladder heights, not raw ones.** A client's reported playback ceiling need not be a rung — for example, a native player may report 1344. `SnapToDeliverableHeight` reduces it to the rung below (1344 → 1080), and `qualityLossPenalty` scores against that. Scored raw, a 4K transcode looked like a perfect fit while a 1080p part that direct-plays was penalised for missing 1344, so the server burned a GPU transcode to deliver the 1080p already on disk. A part's height for scoring is its widescreen class (`OutputSize.HeightClass`: 3840x1540 is 2160, a 1080x1920 phone video 1080), so a wide film scores like the "4K" its label says.

The capability test is against the client's **declared playback box** (`OutputSize.ClientBox`: `requestedMaxWidth` x `requestedMaxResolution`, widescreen when only the height is sent, 3840x2160 when neither is): a source wider *or* taller than it needs a transcode. That box is what the player can decode or what the viewer picked. Each option logs `ClientMaxHeight` / `DeliverableHeight` / `OutputHeight` in `EvaluatedOptions` so a report of this class answers itself.

**The screen is a hint, not a limit** (issue #150; PR #158 found it). A client may also send its screen as `displayWidth` / `displayHeight` (`DisplayHint`). The screen never makes the video need a transcode, because a browser can decode a 4K file and scale it down itself. It is used in two places:
- **Sizing a transcode that has to happen anyway** (codec, HDR display, bitrate, burn-in): the output box is the client box narrowed to the screen (`OutputSize.ScreenBox`).
- **Judging "the same result" between versions:** the heavy `qualityLossPenalty` scores against the screen's rung, so a 4K version that would have to be converted is no better than a 1080p file that plays on a 1080p screen. A light `beyondScreenPenalty` (1 per line between the delivered height and the client box's rung, above the screen) keeps each profile's "the largest version that plays wins". Without a screen hint the two boxes are the same and nothing changes.

A 3832x1640 HDR film on a 1440p HDR monitor is copied with only its DTS audio converted. On a 1440p SDR monitor it is tone-mapped at 2560x1096 (9.6 Mbps) rather than at its own size (17.7 Mbps).

## Output size and bitrate

Every decision works from the **measured** picture (`MediaVideoTrack.Width` / `Height` / `FrameRate`, read by ffprobe at scan time; `OutputSize.Source`). The part's `Resolution` comes from the file name (`1080p`, `4k`), so it is only a fallback: before sizes were stored, a 4K file with no tag in its name was treated as 480p and a transcode of it came out 853x480. A part scanned before the columns existed is probed once when it is next decided on (`StreamManager.FillVideoGeometryAsync` → `IMediaAnalyzerService.ReadVideoStreamsAsync`, a 30-second-capped `ffprobe -select_streams v`), saved, and never probed again; the same probe corrects a bare `DoVi` label (`docs/hardware-acceleration.md`).

**Size** (`OutputSize.Fit`, the same rule as Jellyfin's `GetFixedOutputSize` and Plex's `videoResolution` box): the picture is fitted inside the client's box, keeping its shape, never enlarged, at most 4096 on a side, in even numbers. The box is narrowed by `HdrTranscodeDownscale = Always` (1920x1080) and, when a bandwidth limit applies, by the widest picture that limit carries (`MaxWidthForBitrate`, Jellyfin's ladder in H.264 terms per frame: 1280 up to 6 Mbps, 1920 up to 13.5, 2560 up to 28). The result is `OutputWidth` / `OutputHeight` on the decision and the session, and the transcoder scales to exactly that size (`ScaleTarget`), or not at all when it equals the source. Issue #145's 3840x1540 film on a 2560x1440 screen is 2560x1026; it used to be 3590x1440, wider than the screen. An ultrawide 3440x1440 screen gets 3440x1380.

**Bitrate** (`VideoBitrate.For`): the source's video bitrate, then
- scaled by the smaller picture, as pixels^0.75 (larger frames need fewer bits per pixel; Plex and Jellyfin keep the source bitrate when they shrink a picture);
- raised for the less efficient output codec (HEVC → H.264 x1.67, AV1 → H.264 x2; never lowered) and for 10-bit → 8-bit (x1.5 to x1.05 by bitrate), with Jellyfin's floors for low-bitrate sources and no codec scaling from 30 Mbps;
- capped by a `BandwidthOptimized` ladder and at 400 Mbps.

The encoder **aims at 85% of any bandwidth limit** and its peak cap is the target / 0.85, never above the limit. Plex and Jellyfin aim at the cap itself, so a segment can run past the connection; the headroom keeps the player's buffer filling. `BandwidthKbps` on the session is the peak cap (what upload accounting adds up); the transcoder recovers the target as 85% of it (`VideoBitrate.TargetFromCap`). Encoder flags are in `docs/hardware-acceleration.md`.

**Measured connection** (`ConnectionLimit`). The web client downloads a sample from `GET /api/streaming/bandwidth-sample?kb=` (random bytes, so compression cannot flatter it), 512 KB and then one sized to take about a second (`utils/connectionSpeed.ts`). It does this when the client opens and then hourly, never while a stream starts, and sends the result as `measuredBandwidthKbps`. Under Client Preference and Bitrate, 80% of it becomes the bitrate limit when it is lower than the others ("Measured Connection"). A figure of 300 Mbps or more sets no limit. Direct ignores it, because its promise is to convert only when nothing plays. The viewer's own bitrate pick and the remote and upload limits still apply in every profile.

Native clients should send `requestedMaxWidth` beside `requestedMaxResolution` only for a real limit (a decoder that cannot take 4K, or the viewer's pick); without the width the server assumes a widescreen box. The screen goes in `displayWidth` / `displayHeight`. The web client's **Maximum resolution → Original** sends no limit (the default box is 3840x2160) and the screen, in physical pixels, as the hint. A picked resolution sends that height and its widescreen width as the limit.

## Keeping HDR in a transcode

A transcode used to tone-map every HDR source to SDR H.264. It now keeps HDR10 or HLG as 10-bit HEVC when all of these hold (`BestPathDecisionManager.WhyHdrIsNotKept`):
- **Server:** Keep HDR when transcoding is on (`ServerSetting.PreserveHdrInTranscodes`, default on), and HEVC Video Encoding is not Never (HEVC sources only allows it for an HEVC source).
- **GPU:** the transcoding GPU passed the 10-bit HEVC test (`HardwareAccelerator.EncodesHdr`, `docs/hardware-acceleration.md`), and hardware encoding is on.
- **Player:** it shows the format (`SupportedHdrFormats`; HDR10+ and Dolby Vision with an HDR10 or HLG base qualify as that base), plays HEVC and takes it in HLS, and has no 8-bit limit.
- **No subtitle is burned in** (the overlay runs on 8-bit frames).

A kept transcode has `TargetVideoCodec = "hevc"` and `OutputHdrType` HDR10 or HLG. Its bitrate is HEVC's (`VideoBitrate`: no H.264 codec or bit-depth factor), and the bitrate ladder for the width is HEVC's. `HdrTranscodeDownscale = Always` does not apply to it, because that setting exists to make tone mapping cheaper. When HDR is not kept, the reason says why ("HDR10 is tone-mapped to SDR because the server has no GPU that encodes 10-bit HEVC").

The transcoder checks again with the hardware it actually got (`FFmpegTranscodeService.KeepsHdr`): a session that falls to the CPU (the GPU limit reached, or the retry after a GPU failure) is tone-mapped to SDR H.264 and logged. A Dolby Vision profile 5 file has no base layer to keep and is tone-mapped as before.

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

**Only finished segments are served.** The HLS muxer writes each segment under a temporary name (`-hls_flags temp_file`) and renames it when it is finished, so an encoded segment is served the moment its `.ts` exists (`IsSegmentSealed`). It used to wait 250 ms to see whether the file was still growing, which every first segment after a start or a seek paid. The segment muxer writes in place. So a copied segment is written as `{key}_{n}.part`, FFmpeg lists it in `_ffmpeg_{key}.list` once it is closed, and `PublishFinishedSegments` renames listed ones to `.ts`, replacing an earlier run's copy in one step. A copy session counts a segment as ready only when its `.ts` exists. A relaunch first publishes what the killed run finished, then deletes the list and the `.part` files left behind.

**MPEG-TS, not fMP4.** With fMP4 the timestamp offset lands in the edit list rather than the fragments, and with `-copyts` the first HEVC fragment after a restart came out 0.126 s late. TS restarts are exact, and hls.js plays HEVC from TS.

**The end of the stream.** The plan comes from the file's length, which is its longest track, so an audio or subtitle track running past the video gives a last planned segment with no video in it, and FFmpeg never writes one. The player asked for it until it gave up and sat on the last frame, with no Up Next and Skip Credits frozen too (issue #159). Now an even plan folds a final stretch under 0.5 s into the segment before it, as a keyframe plan already did. When an FFmpeg run exits cleanly at the end of the file, `FileEndsAfterSegment` records its last segment (the highest on disk, or the one before the run's start if it wrote none). A request past it is `SegmentAvailability.PastTheEnd`, which the segment route answers at once with **410 Gone** instead of relaunching FFmpeg. (A 404 already means "not yet", for the player's pre-seek probe.) The web player stops loading on a 410 and counts the item as finished (`usePlaybackFinished`, which also counts a stall within 6 s of the end), so Up Next takes over. A file with no stored length is measured with ffprobe (`SourceDuration`) before it is planned; the 4-hour placeholder is left only for a file ffprobe can't read.

The seek-restart rules count **seconds, not segments**, because copied segments vary in length: a request within 4 minutes of where the current run started waits for it, and a request more than 20 s behind it is a real seek once something has been served.

Tests: `SegmentPlanTests`, `KeyframeReaderTests` (built EBML and MP4 boxes), `KeyframeSegmentArgumentTests` (pinned FFmpeg arguments), `SegmentPublishingTests`, and `KeyframeSegmentFileTests`, which runs real FFmpeg on H.264 and HEVC in MKV and MP4 with uneven keyframes: the index must match ffprobe, every segment must start on its planned keyframe, and two restarts must write the same segments as a whole run. It is skipped without ffmpeg and ffprobe on PATH.

## Throttling

A transcode stops working ahead of the viewer once it has a buffer written. `TranscodeThrottleWorker` calls `ITranscodeService.ThrottleAsync` every 5 seconds. For each encoding session it measures how far the last finished segment runs past the one the player last fetched (`LastServedSegment`, set by `NotifySegmentServed`), and `TranscodeThrottle.Decide` says what to do:

- **Pause** at `TranscoderThrottleBuffer` seconds ahead (Server Settings → Core → Transcoder → Throttle Buffer, default 60, never under 30). FFmpeg pauses on `p` written to its stdin and resumes on `u`. Only jellyfin-ffmpeg understands them; the Docker image ships it (`HostEnvironmentDetector.HasJellyfinFfmpeg`), and without it nothing pauses.
- **Resume** when the player is within half the buffer of the edge, so it doesn't flap at the line, and at once when the player asks for a segment that isn't written yet (`EnsureSegmentAvailableAsync`), whether it caught up or seeked.
- **Leave alone** copied video (cheap to run to the end), a setting of 0, and a session the player hasn't fetched from yet.

A paused FFmpeg keeps its process and decoder state, so resuming costs nothing, and idle eviction still stops it like any other session. Segments are written under a temporary name (`-hls_flags temp_file`) and renamed when finished, so a paused run never leaves a half-written `.ts` for the seal check to take as finished. Every pause and resume is logged with its reason.

Plex throttles this way by default. Jellyfin has the same pause and resume but leaves it off, with a 60-second minimum.

Tests: `TranscodeThrottleTests` (the decision) and `TranscodeThrottlingFileTests`, which runs real jellyfin-ffmpeg: a transcode far ahead pauses and writes nothing, then carries on once the player catches up. It is skipped without jellyfin-ffmpeg on PATH.

## Watch state

A progress ping writes `UserMediaState` through `StreamRepository.UpdateUserMediaStateAsync`, and `WatchStateTransition` decides what it means:

- past **90%** → played, resume cleared to 0
- a re-watch that gets **30 seconds** in → **not** played any more, resume where the viewer is
- before **30 seconds** (`MinimumResumeSeconds`) → no resume point, so a peek gives no Resume button or Continue Watching tile
- anything else → left as it was

`IsPlayed` used to be `IsPlayed || completed`, so it could only ever go true. Re-watching a finished item stored a resume position nothing surfaced, because all three consumers require it to be false: the details page (`inProgress = resumePos > 0 && !isPlayed`), the watched check, and the Continue Watching query (`ResumePositionSeconds > 0 && !s.IsPlayed`). The only escape was marking it unwatched by hand.

The 30-second window (`RestartGraceSeconds`, equal to the minimum resume) exists because a ping can land at position 0 before the player has seeked to its resume point — clearing on that would wipe a finished item's state for someone who never restarted it. An unknown duration (Live TV) leaves the flag alone entirely.

## Watch history

Admin → Watch History (`GET /api/streaming/admin/history`, `StreamHistoryProjection`) lists stream sessions grouped by profile, device and title for each **local day** in the server time zone (`ScheduleClock`, `AT TIME ZONE` in SQL). Grouping by UTC day used to fold an evening's watch into the next morning's sessions for anyone west of Greenwich, and gave the row the newest session's date with the oldest session's time.

- **Everything filters on the server, then pages.** `profileId`, `libraryIds` and `streams` (`DirectPlay`, `DirectStream`, `Transcode`; comma-separated) narrow the sessions before grouping. Groups are ordered by their most recent session, with ties broken by item, profile and device so a row can't move between pages, and `total` counts groups. The page used to load page 1 only and filter those rows in the browser.
- **Profile, then account.** A row names the profile that watched and, when the two differ, the account beneath it. A profile keeps the name it was created with when the account is renamed. `profileId` filters on the server; the dropdown lists every profile from `GET /api/users`, grouped by account.
- **Title.** An episode row leads with the show; the episode number and title sit beneath it (`S02E01 · Ooze`).
- **IP and location** are the stream's own: `StreamSession.IpAddress` / `Location`, written when the stream starts. `StreamingEndpoints` passes `HttpContext.GetClientAddress()` to `StreamManager`, because the device row is only refreshed every five minutes and can still hold the previous network. The location is the device's when the address matches the device's last one, and otherwise comes from `IClientLocationResolver` (the same lookup `DeviceTrackingMiddleware` uses; a public address is remembered for 12 hours). The same address decides whether the stream counts as remote for the bandwidth limits. A session started before these columns existed has neither, and shows the device's current address and location (`ClientDevice.LastIpAddress` / `Location`) as it did before; a session with an address but no location shows no location, not the device's. Now Playing reads the same fields.
- **Duration** is time played: elapsed time minus paused time (`WatchProgress.WatchedSeconds`).
- **Status** (`WatchProgress.PercentComplete`) trusts where a session stopped only when at least half of the stretch it moved across was actually played; otherwise only the minutes played count, from where it started. Skipping an intro or the credits still finishes a title, while jumping to the end in thirty seconds doesn't. A grouped row shows the furthest any of its sessions got. The length is the played part's (`MediaPartId`), falling back to the item's analysis.

## Playback info

The player's info button opens **Playback info**: how this stream is playing, then the playing file (the same `MediaInfoDialog` as the details page, given a `title` and a `lead`).

- **The server's side:** `GET /api/streaming/sessions/{id}/stats` (`PlaybackStatsManager` → `PlaybackStats.Build`; only the account that started the stream). The panel asks every 2 seconds while open.
  - **Source:** container and bitrate, then the video's codec, profile, level, bit depth, HDR, size, frame rate and bitrate, and the audio's language, codec profile (`DTS-HD MA`), channels and layout, sample rate and bitrate.
    - Level, audio profile, sample rate and stream bitrates aren't stored, so `IMediaAnalyzerService.ReadStreamFactsAsync` probes the file the first time it's asked. `StreamFactsCache` keeps 64 files for 30 minutes.
  - **Output:** each stream's strategy, the output codec, size and HDR from the session, and from the transcoder the target and peak bitrate, tone mapping, encoder and decoder.
  - **Transcoder:** `ITranscodeService.GetStatus` reads it from the FFmpeg command that was launched (`TranscodeSummary`), so it can't disagree with what runs. FFmpeg's progress lines (`fps=… speed=…x`, `TranscodeProgress`) give the live frame rate and speed, and stay out of the stderr kept for error reports. It also says when the transcode has finished the file, is paused by throttling (so its last speed isn't shown as current), or moved to the CPU after a GPU error.
- **Why:** `/streaming/start` returns the decision's `reasons` (`StreamDecisionDto.Reasons`, the sentences the joined `Reason` is made of), and the player keeps them on `currentMedia`.
- **The player's side**, read each second: hls.js, native HLS or the file; hls.js's bandwidth estimate; seconds buffered ahead; dropped frames (`getVideoPlaybackQuality`); and a text subtitle the player draws itself.

The wording is in `utils/playbackStats.ts`. Tests: `PlaybackStatsTests`, `TranscodeSummaryTests` (against real built arguments), `StreamFactsReadingTests`, `playbackStats.test.ts`, `PlaybackStatsSections.test.tsx`.

## Gotchas

- **Concurrent sessions on the same title share a transcode.** `_activeTranscodes` is keyed by media item id, so two clients playing the same movie collide on the video. Subtitles are not affected — the cache is keyed on (part, track), so two viewers with different subtitle choices get different files.
- **The subtitle route needs the auth header**, so a client can't point a `<track src>` or a native player's URL loader straight at it. Fetch it through the normal API client and hand the player the bytes (on web, a blob URL).
- **A first request can take as long as the extraction.** That is the trade for never blocking `/start`; it costs once per (part, track), and the client owns the request so it can show its own pending state.
- **Token scope and temp-dir defaults live on `StreamManager`** (`PlayTokenScope`, `HlsTokenScope`, `HlsTokenTtl`, `DefaultTranscodeTempDirectory`) and `StreamingEndpoints` references them. They were duplicated literals; a silent drift 404s every subtitle.
