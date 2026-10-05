# Streaming

How a play request becomes bytes: the path decision, the session, the HLS output and watch state. How subtitles reach the screen is in [`docs/subtitles.md`](subtitles.md).

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

`OutputResolution` / `OutputHdrType` are what the client actually receives, distinct from the source part's `Resolution` / `HdrType`. A 4K HDR source under `HdrTranscodeDownscale = Always` becomes 1080p SDR, and the player badges, admin Now Playing, and Watch History all read the output values so the UI doesn't claim 4K.

**Scoring compares ladder heights, not raw ones.** A client's reported ceiling is often not a rung the encoder can emit — a phone panel reports 1344, a browser window whatever the user dragged it to. `SnapToDeliverableHeight` reduces it to the rung below (1344 → 1080), and both `effectiveTargetHeight` and `qualityLossPenalty` use that snapped value. Scored raw, an option downscaling 4K to "1344p" looked like a perfect fit while a 1080p part that direct-plays was penalised 2640 for missing a height no file and no encoder can produce — so the server burned a GPU transcode to deliver the 1080p already sitting on disk. The rungs must stay identical to `FormatHeightAsResolution`, or the resolution a decision declares and the one it scored disagree again.

`clientMaxRes` stays **raw** for the capability test (`trackHeight > clientMaxRes` → needs transcode) and its reason string: that one is about what the device can decode and display, and snapping it would let a 1440p source through to a 1344p panel. Each option logs `ClientMaxHeight` / `DeliverableHeight` / `OutputHeight` in `EvaluatedOptions` so a report of this class answers itself.

Subtitle strategies — `None` / `BurnIn` / `DirectPlay`, which track plays when nobody picked one, and why the HLS output carries no subtitles — are in [`docs/subtitles.md`](subtitles.md).

## The transcode directory is flat, namespaced by filename

There is no per-session directory. Everything for a session lands in one directory (`ServerSetting.TranscoderTempDirectory`, default `/transcode`) and is namespaced by a **filename prefix**: the transcode key, which is `session.ExtraId ?? session.MediaItemId` — an extra transcodes under its own id so its stream can't collide with its parent movie's.

```
{key}.m3u8              VOD playlist, written up-front from the source duration
{key}_{n}.ts            4-second segments (SegmentSeconds = 4)
_ffmpeg_{key}.m3u8      FFmpeg's own internal playlist
```

The **subtitle cache** is deliberately *not* in here — it lives in a `subcache/` subdirectory, is keyed on content rather than session, and is served by its own route (see below).

`GET /api/streaming/hls/s/{token}/{fileName}` serves a file only when **`fileName.StartsWith(<the prefix the token signs>)`** and the extension is one of `.m3u8`, `.ts`, `.m4s`, `.mp4`, `.vtt`. That is the whole authorization model for session output.

Two consequences to keep in mind when adding a new artifact:

- **Name it `{key}…`** or it is unreachable.
- **Don't end the name with `_<integer>`.** `TryParseSegmentRequest` reads `{guid}_{int}` as a segment request and routes it through the seal-and-wait path, so a file named `{key}_1.vtt` would be treated as a segment that never arrives.

The subtitle route — the cache, the FFmpeg call, sidecar files, pre-extraction on scan, Find Subtitles provider plugins, retention and invalidation, client support — is in [`docs/subtitles.md`](subtitles.md).

## Watch state

A progress ping writes `UserMediaState` through `StreamRepository.UpdateUserMediaStateAsync`, and `WatchStateTransition` decides what it means:

- past **90%** → played, resume cleared to 0
- a re-watch that gets **15 seconds** in → **not** played any more, resume where the viewer is
- anything else → left as it was

`IsPlayed` used to be `IsPlayed || completed`, so it could only ever go true. Re-watching a finished item stored a resume position nothing surfaced, because all three consumers require it to be false: the details page (`inProgress = resumePos > 0 && !isPlayed`), the watched check, and the Continue Watching query (`ResumePositionSeconds > 0 && !s.IsPlayed`). The only escape was marking it unwatched by hand.

The 15-second window exists because a ping can land at position 0 before the player has seeked to its resume point — clearing on that would wipe a finished item's state for someone who never restarted it. An unknown duration (Live TV) leaves the flag alone entirely.

## Gotchas

- **Concurrent sessions on the same title share a transcode.** `_activeTranscodes` is keyed by media item id, so two clients playing the same movie collide on the video. Subtitles are not affected — the cache is keyed on (part, track), so two viewers with different subtitle choices get different files.
- **The subtitle route needs the auth header**, so a client can't point a `<track src>` or a native player's URL loader straight at it. Fetch it through the normal API client and hand the player the bytes (on web, a blob URL).
- **A first request can take as long as the extraction.** That is the trade for never blocking `/start`; it costs once per (part, track), and the client owns the request so it can show its own pending state.
- **Token scope and temp-dir defaults live on `StreamManager`** (`PlayTokenScope`, `HlsTokenScope`, `HlsTokenTtl`, `DefaultTranscodeTempDirectory`) and `StreamingEndpoints` references them. They were duplicated literals; a silent drift 404s every subtitle.
