# Streaming and subtitles

How a play request becomes bytes: the path decision, the session, the HLS output, and how subtitles reach the screen.

## The pipeline

```
POST /api/streaming/start        → BestPathDecisionManager picks part + tracks + strategy
                                   StreamManager creates a StreamSession
                                   returns StreamUrl (+ SubtitleUrl)
GET  /api/streaming/play/{id}?t= → DirectPlay: serves the file
                                   otherwise: starts FFmpeg, redirects to the playlist
GET  /api/streaming/hls/s/{token}/{file} → playlist, segments, and the subtitle sidecar
```

`/play` and the HLS route are **not** `RequireAuthorization`. They are gated by short-lived HMAC tokens from `IStreamingTokenSigner`, so a `<video src>` works without carrying an auth header. `/start` and `/start-extra` are authenticated normally.

## The decision

`BestPathDecisionManager.DetermineBestPathAsync` scores every (part × audio track) combination against the client's declared capabilities and the server's transcode settings, then takes the lowest penalty. Each option carries a `StreamStrategy` (`DirectPlay` / `Remux` / `Transcode`) plus separate `VideoStrategy`, `AudioStrategy`, and `SubtitleStrategy` strings, because the three can disagree — a session can copy video and transcode audio.

`OutputResolution` / `OutputHdrType` are what the client actually receives, distinct from the source part's `Resolution` / `HdrType`. A 4K HDR source under `HdrTranscodeDownscale = Always` becomes 1080p SDR, and the player badges, admin Now Playing, and Watch History all read the output values so the UI doesn't claim 4K.

## Subtitle strategies

`SubtitleStrategy` is one of three values, decided purely by the selected track's codec:

| Value | When | How it reaches the player |
|---|---|---|
| `None` | no subtitle selected | — |
| `BurnIn` | image subtitle — `pgssub`, `hdmv_pgs_subtitle`, `dvd_subtitle`, `vobsub` | painted into the video by FFmpeg (`-vf subtitles=`), which forces a video transcode |
| `DirectPlay` | anything else (text: `subrip`, `ass`, `mov_text`, …) | sidecar WebVTT, below |

Burn-in runs on CPU only, so it is excluded from the full-GPU pipeline.

**The HLS output carries no subtitles at all** — `BuildFFmpegArguments` emits `-sn` unconditionally. Mapping an arbitrary subtitle stream into mpegts either errors or produces bytes no player renders, and Vora doesn't generate in-band WebVTT. Text subtitles ship beside the segments instead.

## The transcode directory is flat, namespaced by filename

There is no per-session directory. Everything for a session lands in one directory (`ServerSetting.TranscoderTempDirectory`, default `/transcode`) and is namespaced by a **filename prefix**: the transcode key, which is `session.ExtraId ?? session.MediaItemId` — an extra transcodes under its own id so its stream can't collide with its parent movie's.

```
{key}.m3u8              VOD playlist, written up-front from the source duration
{key}_{n}.ts            4-second segments (SegmentSeconds = 4)
_ffmpeg_{key}.m3u8      FFmpeg's own internal playlist
{key}_subtitles.vtt     sidecar subtitle, when there is one
```

`GET /api/streaming/hls/s/{token}/{fileName}` serves a file only when **`fileName.StartsWith(<the prefix the token signs>)`** and the extension is one of `.m3u8`, `.ts`, `.m4s`, `.mp4`, `.vtt`. That is the whole authorization model for session output, and it is why a sidecar can't be called `subtitle.vtt`: it would fail the prefix check and collide across sessions.

Two consequences to keep in mind when adding a new artifact:

- **Name it `{key}…`** or it is unreachable.
- **Don't end the name with `_<integer>`.** `TryParseSegmentRequest` reads `{guid}_{int}` as a segment request and routes it through the seal-and-wait path, so a file named `{key}_1.vtt` would be treated as a segment that never arrives. `_subtitles` is safe because it doesn't parse as an integer.

## The sidecar WebVTT pipeline

`StreamManager.PrepareSidecarSubtitleAsync` runs at session start, for both `/start` and `/start-extra`.

1. No subtitle selected, or `IsSubtitleBurnIn` — **delete any existing sidecar** for this key and return null. Declining to name a stale file isn't enough; anyone holding a prefix token could still fetch it.
2. Otherwise resolve the session's `SubtitleTrackId` against the part's subtitle tracks **sorted by `StreamIndex`**, taking both the absolute stream index and the track's 0-based ordinal among the file's subtitles.
3. `ISubtitleExtractionService.ExtractWebVttAsync` shells out to FFmpeg (see below) and returns the file name, or null.
4. On success, sign an `hls`-scope token over the transcode key and return `/api/streaming/hls/s/{token}/{key}_subtitles.vtt` as `StartStreamResponse.SubtitleUrl`.

`SubtitleUrl` is null for Off, for burn-in, and when extraction fails — a subtitle that can't be produced costs the subtitle, not the stream.

### The FFmpeg call

```
ffmpeg -y -i <source> -map 0:<streamIndex> -c:s webvtt -f webvtt <output>
```

- **`-map 0:<streamIndex>` is the absolute stream index**, the same form `BuildFFmpegArguments` uses for video and audio. `MediaSubtitleTrack.StreamIndex` is the stream's index in the file, not its position among subtitles. `0:s:N` means something different and would pick the wrong track whenever the subtitle isn't the Nth subtitle.
- **`-f webvtt` names the muxer explicitly** rather than letting FFmpeg infer it from the `.vtt` extension.
- **Fallback**: if that attempt *runs and exits non-zero*, it retries once as `-map 0:s:<ordinal>`. A timeout or a failed launch does not retry — that would buy a second 30-second wait inside `/start` on the way to the same answer. An exit-0-with-empty-output doesn't retry either; that usually means the track has no cues, which the other map form won't change.
- The ordinal **must** be counted in stream order. FFmpeg numbers `0:s:N` by ascending stream index, while `part.SubtitleTracks` arrives in whatever order EF materialised it. Taking the ordinal from the unsorted collection puts the retry on a different track than the user picked — the same wrong-track bug the fallback exists to fix.
- Capped at 30 seconds, process killed on timeout. Each attempt logs its full argument list at Information; success logs the output path and byte size.

Only **embedded** subtitle tracks exist. `MediaSubtitleTrack` carries a `StreamIndex` and nothing else, and the only thing that creates one is ffprobe analysis of the media file — nothing scans for sidecar `.srt`/`.ass` files on disk. (`SubtitleFormat` in `Vora.Domain.Enums` is dead and unreferenced.)

### Cleanup

`StopProcessAndCleanFilesAsync` takes a `removeSidecars` flag:

- **true** from `StopTranscodeSessionAsync` (so also idle eviction) and the orphan reaper — the session is over.
- **false** from `StartTranscodeSessionAsync`. This matters: `/play` wipes the media's artifacts when it launches FFmpeg, and the sidecar was written earlier by `/start`. Deleting it there would break subtitles on every session.

Seek-restart uses `KillProcessOnlyAsync` and never wipes files, so seeking keeps both the already-encoded segments and the sidecar.

### Client support

**No client renders the sidecar yet.** The web player has no `<track>` element — it renders a `SUB: ENG (subrip)` chip in the player badge bar and nothing more. Wiring `SubtitleUrl` into the player is a separate change, on web and on the native clients.

## Gotchas

- **Concurrent sessions on the same title share a transcode.** `_activeTranscodes` is keyed by media item id, so two clients playing the same movie collide — including on the sidecar, which means the second session's subtitle choice overwrites the first's. Pre-existing to the subtitle work; a per-session key would be the fix.
- **`/start` waits on extraction.** Fire-and-forget would hand back a URL that 404s if the player fetches before FFmpeg finishes. Text extraction is a demux with no decode, normally well under a second.
- **Token scope and temp-dir defaults live on `StreamManager`** (`PlayTokenScope`, `HlsTokenScope`, `HlsTokenTtl`, `DefaultTranscodeTempDirectory`) and `StreamingEndpoints` references them. They were duplicated literals; a silent drift 404s every subtitle.
