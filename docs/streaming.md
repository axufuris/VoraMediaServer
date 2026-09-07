# Streaming and subtitles

How a play request becomes bytes: the path decision, the session, the HLS output, and how subtitles reach the screen.

## The pipeline

```
POST /api/streaming/start        → BestPathDecisionManager picks part + tracks + strategy
                                   StreamManager creates a StreamSession
                                   returns StreamUrl
GET  /api/streaming/play/{id}?t= → DirectPlay: serves the file
                                   otherwise: starts FFmpeg, redirects to the playlist
GET  /api/streaming/hls/s/{token}/{file} → playlist and segments

GET  /api/streaming/sessions/{sessionId}/subtitle/{trackId}.vtt
                                 → text subtitle, extracted on demand and cached
```

`/play` and the HLS route are **not** `RequireAuthorization`. They are gated by short-lived HMAC tokens from `IStreamingTokenSigner`, so a `<video src>` works without carrying an auth header. `/start`, `/start-extra`, and the subtitle route are authenticated normally.

The subtitle route is deliberately **off the video path**. Nothing about starting or serving the stream depends on it, so a slow extraction can't stall or time out playback.

## The decision

`BestPathDecisionManager.DetermineBestPathAsync` scores every (part × audio track) combination against the client's declared capabilities and the server's transcode settings, then takes the lowest penalty. Each option carries a `StreamStrategy` (`DirectPlay` / `Remux` / `Transcode`) plus separate `VideoStrategy`, `AudioStrategy`, and `SubtitleStrategy` strings, because the three can disagree — a session can copy video and transcode audio.

`OutputResolution` / `OutputHdrType` are what the client actually receives, distinct from the source part's `Resolution` / `HdrType`. A 4K HDR source under `HdrTranscodeDownscale = Always` becomes 1080p SDR, and the player badges, admin Now Playing, and Watch History all read the output values so the UI doesn't claim 4K.

## Subtitle strategies

`SubtitleStrategy` is one of three values, decided purely by the selected track's codec:

| Value | When | How it reaches the player |
|---|---|---|
| `None` | no subtitle selected | — |
| `BurnIn` | image subtitle — `pgssub`, `hdmv_pgs_subtitle`, `dvd_subtitle`, `vobsub` | painted into the video by FFmpeg (`-vf subtitles=`), which forces a video transcode |
| `DirectPlay` | anything else (text: `subrip`, `ass`, `mov_text`, …) | WebVTT from the subtitle route, below |

Burn-in runs on CPU only, so it is excluded from the full-GPU pipeline.

**The HLS output carries no subtitles at all** — `BuildFFmpegArguments` emits `-sn` unconditionally. Mapping an arbitrary subtitle stream into mpegts either errors or produces bytes no player renders, and Vora doesn't generate in-band WebVTT. Text subtitles are fetched separately, from the subtitle route.

`StartStreamResponse.SubtitleUrl` still exists on the DTO but is **always null**. It is kept only so the response shape doesn't change; the client builds the subtitle URL itself from the session id and the track id it already has.

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

## The subtitle route

```
GET /api/streaming/sessions/{sessionId}/subtitle/{subtitleTrackId}.vtt
```

**`/start` never touches subtitles.** `StreamManager` has no extraction dependency at all, so there is no path by which starting a session can run FFmpeg. The client asks for a subtitle when it wants one, on its own request, and a slow extraction delays only that request.

The handler:

1. Resolve the session; **404 if it isn't the caller's own** (`session.UserId` against the account on the token). Subtitles are low-stakes, but an authenticated user shouldn't be able to read another user's session by guessing a session id.
2. Resolve the part and its source file — 404 if either is gone.
3. Find the track among the part's subtitle tracks **sorted by `StreamIndex`**, which gives both the absolute stream index and the 0-based ordinal. 404 if the id isn't on this part.
4. 404 if it is an **image** subtitle (`BestPathDecisionManager.IsImageSubtitleCodec`). Those burn into the video; there is nothing to extract, and an empty VTT would be worse than saying no.
5. `GetOrExtractWebVttAsync`, then `Results.File(path, "text/vtt")` — or 404 if extraction produced nothing.

Everything that can't be served is a **404, never a 500**: a missing subtitle is a normal outcome, not a server fault.

Step 4 is why `IsImageSubtitleCodec` is a shared static on `BestPathDecisionManager` rather than a second copy of the codec list. One list decides both whether a track burns in and whether it is extractable; two lists that drift would either burn a track in *and* offer a sidecar for it, or do neither.

### The cache

Keyed on **(`MediaPartId`, `SubtitleTrackId`)** — content, not session — at `{TranscoderTempDirectory}/subcache/{partId}_{trackId}.vtt`. The same file and track give the same VTT for every profile, device, and session, so it is extracted at most once and every later request is a file read.

An entry counts as a hit only if it exists **and is non-empty**. A zero-byte file is what a killed FFmpeg leaves behind, and treating it as a hit would serve an empty subtitle track forever.

Concurrent misses for the same key are serialized by a per-key `SemaphoreSlim`, and the cache is re-checked after taking the gate — so two clients selecting the same subtitle at once cause one FFmpeg run, and the second request returns the first one's output.

### The FFmpeg call

```
ffmpeg -y -i <source> -vn -an -dn -map 0:<streamIndex> -c:s webvtt -f webvtt <output>
```

- **`-map 0:<streamIndex>` is the absolute stream index**, the same form `BuildFFmpegArguments` uses for video and audio. `MediaSubtitleTrack.StreamIndex` is the stream's index in the file, not its position among subtitles. `0:s:N` means something different and would pick the wrong track whenever the subtitle isn't the Nth subtitle.
- **`-f webvtt` names the muxer explicitly** rather than letting FFmpeg infer it from the `.vtt` extension.
- **Fallback**: if that attempt *runs and exits non-zero*, it retries once as `-map 0:s:<ordinal>`. A timeout or a failed launch does not retry — that would buy a second 30-second wait inside `/start` on the way to the same answer. An exit-0-with-empty-output doesn't retry either; that usually means the track has no cues, which the other map form won't change.
- The ordinal **must** be counted in stream order. FFmpeg numbers `0:s:N` by ascending stream index, while `part.SubtitleTracks` arrives in whatever order EF materialised it. Taking the ordinal from the unsorted collection puts the retry on a different track than the user picked — the same wrong-track bug the fallback exists to fix.
- **`-vn -an -dn`** so nothing but the subtitle reaches the output. `-map` already selects it; these stop FFmpeg auto-selecting anything else on the way past.
- **Written to a unique `.tmp` in the cache directory and `File.Move`d into place** only on success, so a half-written file is never at the served name and a failed run leaves no zero-byte entry behind.
- Capped at **180 seconds**. Extraction is I/O-bound — FFmpeg reads the whole container to collect interleaved subtitle packets, so a large remux is slow even though the output is a few KB.
- Logged at Information on both ends: start (source, part, track, stream index) and finish (elapsed ms, output bytes, cache path), plus the full argument list of each attempt.

Only **embedded** subtitle tracks exist. `MediaSubtitleTrack` carries a `StreamIndex` and nothing else, and the only thing that creates one is ffprobe analysis of the media file — nothing scans for sidecar `.srt`/`.ass` files on disk. (`SubtitleFormat` in `Vora.Domain.Enums` is dead and unreferenced.)

### Cleanup

**The transcode service does not touch the subtitle cache.** Its cleanup globs are all `{mediaItemId}_*` in the top-level directory, and the cache is a separate subdirectory keyed on part and track — so entries survive `StopTranscodeSessionAsync`, idle eviction, and the orphan reaper, which is the point of a cache.

Nothing evicts them either. Each is a few KB of text and only exists for a (part, track) someone actually played, so `subcache/` grows slowly; deleting it is safe and just costs a re-extract. If it ever needs bounding, an age-based sweep in `ReapOrphanedTranscodeFilesAsync` is the place.

### Client support

**No client fetches the subtitle route yet.** The web player has no `<track>` element — it renders a `SUB: ENG (subrip)` chip in the player badge bar and nothing more. Calling the route and attaching the result is a separate change, on web and on the native clients.

## Gotchas

- **Concurrent sessions on the same title share a transcode.** `_activeTranscodes` is keyed by media item id, so two clients playing the same movie collide on the video. Subtitles are not affected — the cache is keyed on (part, track), so two viewers with different subtitle choices get different files.
- **The subtitle route needs the auth header**, so a client can't point a `<track src>` or a native player's URL loader straight at it. Fetch it through the normal API client and hand the player the bytes (on web, a blob URL).
- **A first request can take as long as the extraction.** That is the trade for never blocking `/start`; it costs once per (part, track), and the client owns the request so it can show its own pending state.
- **Token scope and temp-dir defaults live on `StreamManager`** (`PlayTokenScope`, `HlsTokenScope`, `HlsTokenTtl`, `DefaultTranscodeTempDirectory`) and `StreamingEndpoints` references them. They were duplicated literals; a silent drift 404s every subtitle.
