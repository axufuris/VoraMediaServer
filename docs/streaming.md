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
                                 → text subtitle, from the cache a scan warmed
                                   (extracted on demand if it isn't there yet)
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

Keyed on **(`MediaPartId`, `SubtitleTrackId`)** — content, not session — at `{TranscoderTempDirectory}/subcache/{partId}_{trackId}_{fingerprint}.vtt`. The same file and track give the same VTT for every profile, device, and session, so it is extracted at most once and every later request is a file read. It lives in the server's own scratch space, never beside the media: the library is treated as read-only.

The **fingerprint** is a short hash of the source's size, its last-write time, and the track's stream index. It is in the file *name*, not in a sidecar record, so validity is structural: a source that changed, or a track that moved to a different stream index after a re-probe, resolves to a name that isn't on disk and is therefore a miss. Nothing has to remember to run a check.

An entry counts as a hit only if it exists **and is non-empty**. A zero-byte file is what a killed FFmpeg leaves behind, and treating it as a hit would serve an empty subtitle track forever.

Concurrent misses for the same key are serialized by a per-key `SemaphoreSlim`, and the cache is re-checked after taking the gate — so two clients selecting the same subtitle at once cause one FFmpeg run, and the second request returns the first one's output. After a successful extraction, any older fingerprint for that same (part, track) is deleted.

### The FFmpeg call

```
ffmpeg -y -i <source> -vn -an -dn -map 0:<streamIndex> -c:s webvtt -f webvtt <output>   # embedded
ffmpeg -y [-sub_charenc CP1252] -i <sidecar> -c:s webvtt -f webvtt <output>             # external
```

A **`.vtt` sidecar is copied**, not converted. Any other sidecar is converted with no `-map` — it has one stream and that stream is the subtitle. FFmpeg assumes UTF-8 and gives up on anything else, which legacy `.srt` files routinely are, so a failed first pass is retried as `CP1252` rather than leaving the track permanently unusable. The first pass deliberately names **no** encoding: guessing one for a file that is already UTF-8 corrupts it.

An external **image** subtitle (`.sub`/`.idx`) has no text to convert and can only be burned in. `PlaybackDecisionVM.SelectedSubtitleExternalPath` carries the path so `-vf subtitles=` points at the sidecar; without it the filter would be handed the video and burn in the wrong track, or none.

- **`-map 0:<streamIndex>` is the absolute stream index**, the same form `BuildFFmpegArguments` uses for video and audio. `MediaSubtitleTrack.StreamIndex` is the stream's index in the file, not its position among subtitles. `0:s:N` means something different and would pick the wrong track whenever the subtitle isn't the Nth subtitle.
- **`-f webvtt` names the muxer explicitly** rather than letting FFmpeg infer it from the `.vtt` extension.
- **Fallback**: if that attempt *runs and exits non-zero*, it retries once as `-map 0:s:<ordinal>`. A timeout or a failed launch does not retry — that would buy a second 30-second wait inside `/start` on the way to the same answer. An exit-0-with-empty-output doesn't retry either; that usually means the track has no cues, which the other map form won't change.
- The ordinal **must** be counted in stream order. FFmpeg numbers `0:s:N` by ascending stream index, while `part.SubtitleTracks` arrives in whatever order EF materialised it. Taking the ordinal from the unsorted collection puts the retry on a different track than the user picked — the same wrong-track bug the fallback exists to fix.
- **`-vn -an -dn`** so nothing but the subtitle reaches the output. `-map` already selects it; these stop FFmpeg auto-selecting anything else on the way past.
- **Written to a unique `.tmp` in the cache directory and `File.Move`d into place** only on success, so a half-written file is never at the served name and a failed run leaves no zero-byte entry behind.
- Capped at **180 seconds**. Extraction is I/O-bound — FFmpeg reads the whole container to collect interleaved subtitle packets, so a large remux is slow even though the output is a few KB.
- Logged at Information on both ends: start (source, part, track, stream index) and finish (elapsed ms, output bytes, cache path), plus the full argument list of each attempt.

### Sidecar subtitle files

A `MediaSubtitleTrack` is either **embedded** (a stream in the container, addressed by `StreamIndex`) or **external** (`ExternalFilePath` set — a file next to the video). Both are ordinary rows, so a sidecar is selectable, streamable, and pre-extractable exactly like an embedded track, and every VM that projects `p.SubtitleTracks` picks it up with no change.

Discovery runs in `MediaAnalyzerManager` and matches `{video base name}[.segments].{ext}` for `.srt`, `.ass`, `.ssa`, `.vtt`, `.sub`. The dot-separated segments give language (`en`, `eng`, `pt-BR`), `forced`, and `sdh`/`cc`/`hi`; there is no hearing-impaired flag on the entity, so that lands in `Title` alongside the language (`EN SDH`) — the picker prefers `Title` over `Language`, so a bare `SDH` would make two languages read alike.

The separator **must** be a literal dot: `Movie (2026) Part 2.en.srt` must not attach itself to `Movie (2026).mkv`, or a viewer gets subtitles that drift against the picture.

Three things follow from external tracks existing that are easy to get wrong:

- **Discovery runs before the probe-skip guard.** Dropping a `.srt` beside an unchanged video changes nothing ffprobe can see, so a pass gated on "did the file change" would never find it.
- **Reconciliation is split.** `SyncMediaTracksAsync` matches embedded tracks by `StreamIndex` and now filters to `ExternalFilePath == null`; sidecars reconcile by path in `SyncExternalSubtitleTracksAsync`. Left together, an ffprobe pass would delete every sidecar — and throw first, since sidecars all share the default stream index.
- **The `0:s:N` ordinal counts container streams only.** External rows are excluded before indexing, in both the endpoint and the pre-extraction pass; counting them shifts every embedded ordinal after them.

An external track's cached VTT is fingerprinted against the **sidecar**, not the video, so re-saving the `.srt` invalidates it while the untouched video stays cached.

`SubtitleFormat` in `Vora.Domain.Enums` remains dead and unreferenced — the codec is a string, parsed from the file extension.

### Pre-extraction on scan

`ServerSetting.PreExtractSubtitlesOnScan` (**default on**, System Settings → Video Preview Thumbnails → Subtitles) warms the cache after a scan so the endpoint is a hit before anyone plays anything. It is **the only switch** for the feature and is fully independent of thumbnail generation — either can be on with the other off.

`ISubtitlePreExtractionManager` is its own job with its own triggers, deliberately not chained to the thumbnail step:

- **Library scan** — `RunFullLibraryWorkflowAsync` queues `QueuePreExtractLibrarySubtitles` as its own step. Queued rather than awaited: the pass parks while anything is transcoding, which must not hold a scan open.
- **Single-file ingest** (`QueueScanNewFile`) and **per-item Analyze** — queue `QueuePreExtractMediaItemSubtitles` right after analysis, which is the point at which the item's subtitle tracks are known.
- **Backfill** — `POST /api/metadata/subtitles/backfill` (admin) walks every video library and fills whatever is missing, so an existing library is warmed without a rescan. There is a button for it under the same settings card.

Throttling, because each extraction reads a whole container off the media disk:

- **Concurrency 1.** Every subtitle job shares one task-queue `resourceKey`, so no two run at once however many are queued.
- **Yields to playback.** Before each file the pass polls `ITranscodeService.GetActiveTranscodeCount()` and waits while anything is transcoding (15s backoff, 30-minute ceiling before it proceeds anyway). This is server-wide rather than per-drive — a transcode can't be mapped back to a library cheaply, so the conservative reading is the one implemented.
- Honours the task's cancellation token, so a shutdown or an admin cancel stops it between files.

Only **text** codecs are extracted. Image subtitles (`hdmv_pgs_subtitle`, `pgssub`, `dvd_subtitle`, `vobsub`) are skipped — they burn into the video and have no sidecar. The classification is `BestPathDecisionManager.IsImageSubtitleCodec`, the same list the burn-in decision uses.

### Find Subtitles (provider plugins)

Online subtitle search is a **swappable provider plugin**, the same shape as metadata and ratings: `ISubtitleSearchProvider` in `Vora.Plugins.Interfaces`, registered in `PluginLoaderExtensions.PluginProviderInterfaces`, configured through the ordinary plugin-settings mechanism. `OpenSubtitlesSubtitleProvider` is the built-in implementation.

`SubtitleSearchManager` picks the active one from `ServerSetting.SubtitleSearchProviderId`, **but only while that provider is actually configured** — otherwise it falls through to any installed provider that is. An admin who names a plugin and never enters its key would otherwise hide a second one that works.

```
GET  /api/media/{id}/subtitles/search?languages=en,es   → results
POST /api/media/{id}/subtitles/download {providerFileId, language}
```

Both are authenticated and both **404 when no provider is configured**. An empty list would read as "this title has no subtitles"; a 404 says the feature is not on.

Availability is exposed as `FeatureFlagsVM.SubtitleSearch` so the clients hide the UI — the web player shows a **Find subtitles online** entry under the subtitle picker only when the flag is true, and hides rather than disables it, since a button that can only fail is worse than no button. A download refreshes the track list, selects the new track, and sideloads it through the existing `<track>` path: no restart, no new delivery code. It is **read-only and derived** — it is absent from `UpdateFeatureFlagsRequest`, because it follows the plugin: an admin turns it on by entering an API key, not by flipping a switch.

**A query is built from the item, not the row.** An episode searches on its *series* title plus season and episode numbers — a provider matching on an episode's own title finds nothing — and an external id is preferred over a title, because a fuzzy title match is where wrong-film subtitles come from.

**Downloads land in the server's store, never beside the media**, at `{StoragePaths:Subtitles}/{shard}/{mediaItemId}/{trackId}.vtt`. The library is read-only. The bytes are converted to WebVTT once, on the way in, so the delivery endpoint later serves them as a straight copy.

The result is an external `MediaSubtitleTrack` with `IsDownloaded = true`, which makes it selectable through the machinery already described — and that flag matters: sidecar reconciliation matches by path against what is on disk next to the video, so a downloaded track must be **excluded from `SyncExternalSubtitleTracksAsync`** or every scan would delete it.

### Retention and invalidation

**Nothing is evicted by age or size.** Once extracted, a VTT is kept indefinitely — the point of pre-extraction is that unchanged media is never read twice. Invalidation is driven entirely by the content changing, in two layers:

1. **The fingerprint** (above) makes a stale entry a miss by construction, on both the pre-extraction skip check and the on-demand endpoint. They use the same check, so neither can serve a VTT the other would consider stale.
2. **Identity purges.** `MediaAnalyzerManager` purges a part's cached subtitles when it re-probes that part — which only happens for a new or changed file, and covers a track being dropped from the file. `MediaManager.DeleteMediaAsync` purges an item's parts before the row goes. And because deletions can bypass that path (a dedupe merge dropping a part, for one), the **backfill sweeps orphans**: it is the only pass that sees every live `MediaPart` id, so it is the only one that can tell an orphaned cache file from another library's entry.

### Client support

The web player fetches the route and sideloads the result. Two things shape how:

- **The route needs the auth header**, so `<track src>` can't point at it — a native track load sends no headers and 401s. `streamingService.fetchSubtitleVtt` GETs it through `apiClient` as a Blob and hands the `<track>` an object URL, cached per session + track and revoked when the session ends.
- **Picking a text subtitle doesn't restart the stream.** `GlobalVideoPlayer` keeps exactly one sideloaded `<track>` and swaps its source; the video keeps playing. Only a video/audio change, or adding or dropping a burn-in subtitle, still goes through `changeStreams`. A text subtitle is never sent to `/streaming/start` either — it's attached after playback begins, so a cold extraction can't delay the play request.

Codec classification is duplicated client-side in `utils/subtitleKind.ts` and **must agree with `BestPathDecisionManager.IsImageSubtitleCodec`**: a codec the server burns in but the client thinks is text renders the subtitle twice, and the reverse renders it not at all.

## Gotchas

- **Concurrent sessions on the same title share a transcode.** `_activeTranscodes` is keyed by media item id, so two clients playing the same movie collide on the video. Subtitles are not affected — the cache is keyed on (part, track), so two viewers with different subtitle choices get different files.
- **The subtitle route needs the auth header**, so a client can't point a `<track src>` or a native player's URL loader straight at it. Fetch it through the normal API client and hand the player the bytes (on web, a blob URL).
- **A first request can take as long as the extraction.** That is the trade for never blocking `/start`; it costs once per (part, track), and the client owns the request so it can show its own pending state.
- **Token scope and temp-dir defaults live on `StreamManager`** (`PlayTokenScope`, `HlsTokenScope`, `HlsTokenTtl`, `DefaultTranscodeTempDirectory`) and `StreamingEndpoints` references them. They were duplicated literals; a silent drift 404s every subtitle.
