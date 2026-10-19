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

**Still to add — the audio track's position on the details payload.** Subtitle tracks
carry `embeddedPosition`, but `MediaDetailsPartAudioTrackVM` has no position (nor a
stream index), so mpv could not be told which audio stream to switch to mid-play.
Add `EmbeddedPosition` to it with the meaning `/start`'s `audioTrackPosition` has: the
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

- `desktopBridge.ts` — the `VoraDesktopBridge` interface and every type it references,
  exactly as in `01-architecture.md` (this file is the contract; doc 05 has CI diff
  the shell's copy against it). Plus:
  - `getDesktopBridge(): VoraDesktopBridge | null` — reads `window.voraDesktop`,
    validates `bridgeVersion >= MIN_DESKTOP_BRIDGE_VERSION`, returns null otherwise.
  - `isDesktopPlayback(): boolean` — bridge present **and** `mpv.available` **and**
    the user's "Use mpv for video" desktop setting is on.
- A `Window` augmentation in a `.d.ts` so `window.voraDesktop` is typed without casts.

Respect the existing ESLint boundary rule for `src/api/`: the api layer may import
types from `src/desktop/` but not components.

## C. Vora.Web — device headers

`src/api/client.ts` currently sends `X-Vora-Client: Vora Web`,
`X-Vora-Device: Web Browser`, `X-Vora-Device-Type: Browser`. When the bridge is
present send:

| Header | Value |
| --- | --- |
| `X-Vora-Client` | `Vora Desktop` (QA builds: `Vora Desktop QA`) |
| `X-Vora-Device` | `Vora Desktop (Windows)` / `Vora Desktop (Linux)` |
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

Also check `utils/playQualification.ts` and `utils/playbackDecision.ts`: anything
that predicts "this will transcode" on the client must use the desktop profile when
`isDesktopPlayback()`, or details pages will show wrong quality chips.

## E. Vora.Web — engine abstraction + headless controller

Refactor `components/Player/GlobalVideoPlayer.tsx` (≈1,030 lines) as described in
`01-architecture.md`:

1. `components/Player/engine/PlaybackEngine.ts` — the interface and event types.
2. `components/Player/engine/WebVideoEngine.ts` — the current `<video>` + `loadHls`
   behaviour moved behind the interface. **Do not change browser behaviour**, including
   the resume-seek re-attach rule (time listener re-attaches when the `<video>`
   re-mounts on a sessionId change).
3. `components/Player/engine/DesktopMpvEngine.ts` — forwards to `bridge.player`.
   `selectLocalTracks` returns `true` only when the current session's strategy is
   `DirectPlay` (mpv switches embedded tracks instantly); otherwise `false`, and the
   controller restarts the session as the web player does today.
4. `components/Player/usePlaybackController.ts` — session start/stop, pings,
   resume (from the freshly-loaded details VM, never a nav arg), markers + per-profile
   auto-skip, up-next (`useUpNextEnding`), track and quality changes, scrub-thumbnail
   VTT loading, extras. Everything `GlobalVideoPlayer` decides today.
5. `GlobalVideoPlayer` becomes chrome over `usePlaybackController(webEngine)`.
6. `components/Player/DesktopPlaybackHost.tsx` — mounted instead of the web chrome when
   `isDesktopPlayback()`. Renders nothing visible (a small "Playing in Vora player…"
   placeholder is fine). Builds `PlayerChromeState` from the controller and calls
   `engine.setChromeState` whenever it changes (throttle to ≤10/s; position is not
   part of it — Lua reads position from mpv directly).
7. Handle `ChromeIntent`s from the engine by calling the same controller functions the
   web chrome's buttons call.

`PlayerChromeState` and `ChromeIntent` are defined in `desktopBridge.ts`; their full
shape is in `04-player.md`.

**Tests:** run the existing player tests unchanged (they must pass before and after
the refactor). Add controller tests with a fake engine: resume position, ping cadence,
auto-skip firing once per marker, up-next hand-off, local-vs-restart track switching.

## F. Vora.Web — desktop-only UI

- **Settings → Playback → Desktop player** section, shown only when the bridge exists:
  Use mpv for video (on), Audio passthrough (Off / AC-3 + DTS / All incl. TrueHD,
  DTS-HD, E-AC-3), HDR output (on), Hardware decoding (Auto / Off), Open advanced mpv
  settings folder. Reads/writes through `bridge.settings`. Use the existing admin/client
  settings primitives — no hand-rolled controls.
- **mpv missing banner** (Linux only): when `bridge.mpv.available` is false, a one-time
  dismissible notice explaining video plays in the built-in player and how to install
  mpv (`bridge.mpv.reason` carries the distro-specific text from the shell).
- **Playback failure fallback:** if the desktop engine errors before the first frame,
  `dialog.confirm` "Couldn't start the Vora player — play with the built-in player
  instead?" → restart the session with the browser capability profile and the web
  engine. Log the mpv error text.
- **About / Settings footer:** show `Vora Desktop <shellVersion> (<channel>)`.

## G. Docs to update in this repo

- `docs/frontend-client-ui.md` — the engine split and `DesktopPlaybackHost`.
- `docs/streaming.md` — nothing for image subtitles (PR #175 documented them in
  `docs/subtitles.md` and `docs/track-selection.md`); the desktop engine if the
  playback path changes.
- `docs/auth-and-devices.md` — the desktop header values.
- Root `CLAUDE.md` — already links this folder; add a golden-rule line if the refactor
  creates one (e.g. "player logic lives in `usePlaybackController`, never in an engine").
