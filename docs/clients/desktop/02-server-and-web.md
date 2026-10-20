# 02 — Changes in this repo (Vora.Api + Vora.Web)

Do this phase first, on a feature branch, as one PR. Nothing here changes behaviour
for browsers, Android or Roku: every new field is optional and every new code path is
gated on the desktop bridge being present.

Follow the golden rules in the root `CLAUDE.md` (no `!`/`#pragma`, no comments, VMs
at the boundary, strict TS with no `any`, tokens not palette colours, `useDialog`).

## A. Vora.Api

**Image subtitles — already on `main` (PR #175).** `rendersImageSubtitles` on the
capabilities, `subtitleTrackPosition` on `/start`, `embeddedPosition` on
`MediaDetailsPartSubtitleTrackVM`; the server keeps an embedded image subtitle out of
the picture only when the result is Direct Play. The desktop client sends
`rendersImageSubtitles: true` and selects the embedded track in mpv by position.
**Container families — already on `main` (PR #176).** `ContainerFamily` compares `.m4v`
and `.mov` as `mp4`.

**The audio track's position on the details payload.** Subtitle tracks carried
`embeddedPosition`, but `MediaDetailsPartAudioTrackVM` had no position (nor a stream
index), so mpv could not be told which audio stream to switch to mid-play. Its
`EmbeddedPosition` now has the meaning `/start`'s `audioTrackPosition` has: the
zero-based position among the file's audio streams in stream order.

**Check, don't assume:**

- `X-Vora-Device-Type: Desktop` needs no mapping: the type is free text.
  `DeviceTrackingMiddleware` stores the header as sent and the admin Devices page
  (`AuthorizedDevicesPage`) prints it.
- `DeviceCapsDto` on `main` already has many more fields than the browser profile
  below mentions (`detectedAudioChannels`, `pickedAudioChannels`, `playsAnyAudioLayout`,
  `canSwitchAudioTracks`, `hlsVideoCodecs`, display size, measured bandwidth…). Read
  `docs/track-selection.md` and `docs/streaming.md` and fill every one of them in the
  desktop profile; don't leave any at a default without a reason.

## B. Vora.Web — the bridge types

New folder `src/desktop/`:

- `desktopBridge.ts` — the `VoraDesktopBridge` interface and every type it references
  (`01-architecture.md`), and nothing else: no imports, so the shell keeps a
  byte-identical copy and doc 05's CI diff can compare them.
- `desktopPlayback.ts` (React-free, so `src/api` may use it) — `getDesktopBridge()`
  reads `window.voraDesktop` and returns null below `MIN_DESKTOP_BRIDGE_VERSION`;
  `isDesktopPlayback()` is bridge present **and** `mpv.available` **and** "Use the Vora
  player" on, read from a cache of the app's settings, capabilities and mpv status that
  `loadDesktopPlayback()` fills. `useDesktopPlayback()` is the hook for components.
- `voraDesktop.d.ts` types `window.voraDesktop` without casts.

Respect the existing ESLint boundary rule for `src/api/`: the api layer may import
types from `src/desktop/` but not components.

## C. Vora.Web — device headers

`src/api/client.ts` currently sends `X-Vora-Client: Vora Web`,
`X-Vora-Device: Web Browser`, `X-Vora-Device-Type: Browser`. When the bridge is
present send:

| Header | Value |
| --- | --- |
| `X-Vora-Client` | `Vora Desktop` (QA builds: `Vora Desktop QA`) |
| `X-Vora-Device` | `Vora Desktop (Windows)` / `Vora Desktop (Linux)` (QA builds: `Vora Desktop QA (Windows)` / `Vora Desktop QA (Linux)`) |
| `X-Vora-Device-Type` | `Desktop` |
| `X-Vora-OS` | from `bridge.platform` |

The device id stays the existing persistent localStorage id (per origin, so per server
— that is the same behaviour as a browser and is fine).

## D. Vora.Web — desktop capability profile

New `src/utils/desktopCapabilities.ts`, used by `streamingService.startSession` and
`startExtraSession` instead of `scanDeviceCapabilities()` when `isDesktopPlayback()`:

- `videoCodecs`, `audioCodecs`, `containers`, `maxAudioChannels`: from
  `bridge.capabilities()` (the shell derives them from the mpv build, doc 03). Expected
  result: h264, hevc, av1, vp9, vp8, mpeg2video, mpeg4, vc1 · aac, ac3, eac3, truehd,
  dts, flac, opus, vorbis, mp3, pcm_* · 8 channels. Containers are compared against
  `MediaPart.Container`, which is the **file extension**, so report every extension Vora
  ingests (`MediaFileExtensions.Video` + `HomeVideo`: mkv, mp4, avi, m4v, mov, mts,
  m2ts, 3gp, mpg, mpeg, wmv, webm) plus ts, after confirming mpv opens each.
- `supportedHdrFormats`: **always** `['HDR10', 'HLG', 'HDR10+', 'DolbyVision']` — mpv
  outputs HDR on an HDR display and tone-maps well on an SDR one, so the server must
  never tone-map for this client. These strings normalise in
  `BestPathDecisionManager.NormalizeHdr` (`HDR10+` and the TV apps' `HDR10Plus` both
  read as HDR10+).
- `maxVideoBitDepth`: 12.
- `rendersImageSubtitles`: true.
- `canSwitchAudioTracks`: true (mpv switches embedded tracks in the original file).
- `playsAnyAudioLayout`: true (mpv downmixes locally); `detectedAudioChannels` from the
  shell (8 when passthrough is on, otherwise the OS output layout if the shell can read
  it, else 8 — mpv downmixes anyway).
- `hlsVideoCodecs`: h264, hevc and mpeg2video, the codecs the server copies into its
  MPEG-TS segments for the TV apps (`HlsVideoCarriage`). Add av1 only after confirming
  that AV1 copied into those segments plays in mpv; hls.js and ExoPlayer can't read
  AV1 from MPEG-TS (`docs/track-selection.md`).
- `displayWidth` / `displayHeight`: the screen the window is on, from the shell.
- `requestedClientBitrateKbps`, `requestedMaxResolution`, user caps: reuse the
  existing playback-prefs logic in `hardwareScanner.ts` (`applyUserCaps`) — the user's
  Quality choice still wins. Extract the shared part rather than duplicating it.
- `clientBandwidthKbps`: same as the browser scan.
- `measuredBandwidthKbps`: the same connection measurement the browser sends
  (`utils/connectionSpeed.ts`).

`playbackCapabilities()` picks the profile for `/start`, `/decision`, `/start-extra`
and the device report. The client-side guesses (`playbackDecision.ts`, read by the
details page chips and the player's Quality panel) get it through
`usePlaybackCapabilities`. `playQualification.ts` is about music listens and has no
part in this.

## E. Vora.Web — engine abstraction + headless controller

`components/Player/GlobalVideoPlayer.tsx` (≈1,030 lines) is split as `01-architecture.md`
describes. Where this differs from the first plan, the code on `main` decided:

1. `engine/PlaybackEngine.ts` — `PlaybackEngine` (what the controller drives) and
   `PlaybackTransport` (play, pause, seek, position, stop), one `EngineEvent` union.
2. `engine/WebVideoEngine.ts` — the `<video>` + `loadHls` code moved unchanged.
3. `engine/DesktopMpvEngine.ts` — forwards to `bridge.player`. `selectLocalTracks`
   returns `true` only in a `DirectPlay` session; otherwise the controller restarts it.
4. `usePlaybackController.ts` — markers + per-profile auto-skip, commercial skip,
   up-next, track and version picks, subtitle sideloading, scrub thumbnails, extras:
   everything `GlobalVideoPlayer` decided. Pings, close and session restarts stay in
   `PlayerContext`, which music shares; it reads the attached transport instead of the
   `<video>`. Resume comes from `startPosition`, which the caller takes from the
   details VM it just loaded.
5. `GlobalVideoPlayer` is chrome over `usePlaybackController(webEngine)`.
6. `DesktopPlaybackHost.tsx` — mounted by `MainLayout` instead of the web chrome,
   decided once per session. A small bar; `PlayerChromeState` (`desktopChromeState.ts`)
   goes to mpv at most 10 times a second and only when it changed. Position is not
   part of it — Lua reads it from mpv.
7. `ChromeIntent`s call the controller functions the web buttons call; Up Next starts
   through `useUpNextAutoPlay`, the hook `UpNextOverlay` uses.

`PlayerChromeState` and `ChromeIntent` are defined in `desktopBridge.ts`; their full
shape is in `04-player.md`.

**Tests:** the existing player tests pass unchanged. Controller tests use a fake
engine (resume position, auto-skip once per marker, up-next hand-off, local-vs-restart
track switching); ping cadence is tested on `PlayerContext` with a fake transport.

## F. Vora.Web — desktop-only UI

- **Settings → Playback → Desktop player** (`DesktopPlayerSection`), shown only in the
  app: Use the Vora player (on), Audio passthrough (Off / AC-3 and DTS / All incl.
  TrueHD, DTS-HD, E-AC-3), HDR output (on), Hardware decoding (Automatic / Off), Open
  mpv settings folder (`bridge.settings.openAdvancedFolder`). The same controls as the
  other client settings sections, saved through `bridge.settings`.
- **mpv missing banner** (Linux only, `MpvMissingBanner`): a dismissible notice
  (`desktop_mpv_notice_dismissed`) that video plays in the built-in player, with
  `bridge.mpv.reason`. It goes away by itself when the shell reports mpv found.
- **Playback failure fallback:** when the desktop engine errors, `dialog.confirm`
  "Couldn't start the Vora player — play with the built-in player instead?" (before
  the first frame; "The Vora player stopped — keep watching…" after it) → the session
  restarts at the same position with the browser profile and the web engine
  (`forceBuiltInPlayer`, until the player closes). Declining closes the player.
- **About tab:** `Vora Desktop <shellVersion> (<channel>)`.

## G. Docs to update in this repo

- `docs/frontend-client-ui.md` — the engine split and `DesktopPlaybackHost`.
- `docs/streaming.md` — nothing for image subtitles (PR #175 documented them in
  `docs/subtitles.md` and `docs/track-selection.md`); the desktop engine if the
  playback path changes.
- `docs/auth-and-devices.md` — the desktop header values.
- Root `CLAUDE.md` — already links this folder; add a golden-rule line if the refactor
  creates one (e.g. "player logic lives in `usePlaybackController`, never in an engine").
