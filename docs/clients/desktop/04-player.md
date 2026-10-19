# 04 — mpv playback and the Vora player chrome

## Launching mpv (`mpvHost.ts`)

One mpv process per **playback run** (a movie, or a chain of episodes via up-next).
Spawn on `player.open`, reuse it for `load-next`, quit it on `player.close`.

Config isolation: Vora must not be broken by a user's own `~/.config/mpv` (their
scripts, keybindings or OSC would fight the chrome). Before each spawn, write
`userData/mpv-runtime/mpv.conf` = Vora defaults (from `src/player/mpv.conf` +
`DesktopSettings`) followed by `include="<userData>/mpv/mpv.conf"` (the user's
advanced overrides). Then launch:

```
mpv --config-dir=<userData>/mpv-runtime --load-scripts=no
    --script=<resources>/player/vora      # directory script, main.lua inside
    --input-ipc-server=<pipe-or-socket>   # \\.\pipe\vora-mpv-<pid>  |  $XDG_RUNTIME_DIR/vora-mpv-<pid>.sock
    --idle=yes --force-window=yes --keep-open=yes
    --log-file=<userData>/logs/mpv.log
```

Vora default `mpv.conf` (the CLI should verify each option against the pinned mpv's
`--list-options` and drop/rename any that changed):

```
# chrome
osc=no
osd-bar=no
input-default-bindings=no
input-conf=<resources>/player/input.conf
title=Vora
fullscreen=yes
# video
vo=gpu-next
hwdec=auto-safe                 # 'no' when hardwareDecoding = off
target-colorspace-hint=yes      # HDR output; omitted when hdrOutput = false
tone-mapping=auto
hdr-compute-peak=auto
# Windows only: gpu-api=d3d11   | Linux: leave gpu-api auto (Vulkan on Wayland)
# audio
audio-channels=auto
# passthrough=core → audio-spdif=ac3,dts
# passthrough=all  → audio-spdif=ac3,dts,eac3,truehd,dts-hd
# Windows + passthrough on → audio-exclusive=yes
# subtitles / tracks — the server and controller pick tracks
sub-auto=no
audio-file-auto=no
# network + behaviour
cache=yes
demuxer-max-bytes=512MiB
demuxer-readahead-secs=60
network-timeout=30
save-position-on-quit=no
ytdl=no
```

## IPC (`mpvIpc.ts`)

JSON IPC over the pipe/socket with `net.connect`. Implement `command(args)` with
`request_id` correlation and a timeout, `observe_property`, and an event stream.

Observe: `time-pos`, `duration`, `pause`, `paused-for-cache`, `core-idle`,
`eof-reached`, `track-list`, `video-params`, `video-out-params`, `audio-params`,
`hwdec-current`. Events: `file-loaded`, `end-file` (with `reason`), `shutdown`,
`client-message` (intents from Lua).

Forward to the renderer as `PlayerEvent`s: `time` (throttled to 4/s), `paused`,
`buffering`, `ended` (`eof-reached` true), `error` (`end-file` reason `error`, or the
process dying before `file-loaded`), `intent`, `info` (video/audio params for the Info
panel). mpv process exit → `closed`.

Commands main issues for the renderer:

| Bridge command | mpv |
| --- | --- |
| `open` / `load-next` | `loadfile <url> replace -1 start=<seconds>` (+ `set_property force-media-title <title>`) |
| `play` / `pause` | `set_property pause false/true` |
| `seek` | `seek <seconds> absolute+exact` (relative: `seek <delta> relative`) |
| `tracks` (local switch) | `set_property aid/sid <mpv id>` — see mapping below |
| `sub-add` (external sidecar) | set `http-header-fields` to the bearer header, then `sub-add <url> select <title>` |
| `close` | `quit` (then kill after 3 s if still alive) |

The renderer only ever sends server-origin `http(s)` URLs; `mpvHost` re-validates.

## Track mapping (Direct Play only)

Use the same contract the TV apps use (`docs/track-selection.md`; the subtitle
position is on `main` since PR #175): `/start` returns
`audioTrackPosition` and `subtitleTrackPosition`, the zero-based position of the chosen
track among the file's **embedded** streams of that type, in stream order. For switching
mid-play, subtitle tracks carry it as `embeddedPosition`; audio tracks after doc 02 A.

- mpv lists embedded tracks per type in file order (`track-list`, `type`, `id`,
  `ff-index`, `lang`, `codec`). Pick the entry at that position, then check codec and
  language against the details payload, the way Vora.Android's `audioGroupToSelect`
  does, because a container can order streams differently from what a player keeps.
- External sidecar subtitles (`isExternal` on the details payload) have no `ff-index`; load them with
  `sub-add` from `GET /api/streaming/sessions/{sessionId}/subtitle/{trackId}.vtt`.
- Text and image subtitles both render in mpv; ASS styling is preserved for embedded
  ASS. (Sidecar `.ass` arrives as VTT from that route and loses styling — acceptable
  for v1, note it in the QA results.)
- Non-DirectPlay sessions: `selectLocalTracks` returns false; the controller restarts the
  session as the web player does.

## The chrome (`src/player/vora/*.lua`)

A directory script: `main.lua` plus modules (`state.lua`, `layout.lua`, `draw.lua`,
`input.lua`, `scrub.lua`, `panels.lua`, `icons.lua`). Lua is **render + input only**.

- Draw with `mp.create_osd_overlay('ass-events')` (rounded rects, text, icons as ASS
  vector paths). Generate `icons.lua` at build time from the same SVG icons Vora.Web
  uses (`scripts/build-icons.ts` converts SVG paths to ASS `\p1` drawings) so the icon set
  matches.
- Images (scrub thumbnails) via `overlay-add`: main decodes the server sprite once with
  `nativeImage` to raw BGRA in `userData/tmp/sprite-<sessionId>.bgra`; Lua shows a cell
  with `overlay-add <id> <x> <y> <file> <offset> bgra <w> <h> <stride>`, where `offset`
  selects the cell inside the single sprite file. Thumbnails are server-generated —
  never generate them client-side.
- Read position/duration/pause straight from mpv (`mp.observe_property`) so the scrub bar
  moves smoothly; everything semantic comes from `PlayerChromeState`.
- Receive state: `mp.register_script_message('state', json)` (main sends
  `script-message-to vora state <json>`). Send intents:
  `mp.commandv('script-message', 'vora-intent', json)`.
- Theming: `PlayerChromeState.theme` carries hex colours the renderer reads from the
  active template's CSS tokens (`--vora-accent-500`, `--vora-bg-raised`, text tokens), so
  seasonal templates reach the player too. Font: bundle the web client's UI font in
  `player/fonts/` and point `osd-font` at it (check Vora.Web for the font in use).
- HDR: the chrome is drawn inside mpv's HDR output. Check that white UI isn't blinding
  on an HDR display; if mpv offers an OSD/subtitle HDR peak option on the pinned
  version use it, otherwise cap chrome whites around 80%.

```ts
interface PlayerChromeState {
  title: string; subtitle: string | null;          // "S2 · E5 — Episode name"
  theme: { accent: string; surface: string; text: string; textMuted: string };
  status: 'loading' | 'playing' | 'error';
  statusText: string | null;
  markers: { kind: 'Intro' | 'Recap' | 'Preview' | 'Credits' | 'CreditsScene'; start: number; end: number }[];
  skip: { label: string } | null;                  // "Skip Intro" etc., visible now
  upNext: { title: string; subtitle: string | null; countdownSeconds: number | null } | null;
  audio: TrackOption[]; subtitles: TrackOption[];  // TrackOption = { id, label, detail, selected }
  quality: { options: { id: string; label: string; selected: boolean }[]; currentLabel: string };
  info: { label: string; value: string }[];        // Info panel rows (strategy, codecs, HDR, bitrate…)
  canPlayNext: boolean; canPlayPrevious: boolean;
}

type ChromeIntent =
  | { type: 'toggle-pause' } | { type: 'seek'; to: number } | { type: 'skip-marker' }
  | { type: 'play-next' } | { type: 'play-previous' } | { type: 'cancel-up-next' }
  | { type: 'select-audio'; id: string } | { type: 'select-subtitle'; id: string | null }
  | { type: 'select-quality'; id: string } | { type: 'exit' };
```
Both types live in `desktopBridge.ts`; Lua decodes them with `utils.parse_json`.

## Input (`input.conf` + `input.lua`)

Mouse and keyboard first, like the web player, plus remote/D-pad keys for HTPC use
(the web client already supports D-pad navigation through `useSpatialNavigation`):

| Input | Chrome hidden | Chrome visible |
| --- | --- | --- |
| Mouse move / click | show chrome | buttons, scrub bar (hover shows thumbnail) |
| Space / Enter / Play-Pause key | toggle pause (Enter also shows chrome) | activate focused control |
| Left / Right | seek −10 s / +30 s | move focus; on the scrub bar: scrub with thumbnails, Enter commits |
| Up / Down | show chrome | move between control rows / panels |
| Esc / Backspace / Back | `exit` intent | close panel, then hide chrome |
| Wheel | volume ± | volume ± |
| M, 9/0 | mute, volume | same |
| F / F11 / double-click | toggle fullscreen | same |
| Next / Previous media keys | `play-next` / `play-previous` | same |

Auto-hide timing, layout, button order, icons and wording copy the web player
(`useAutoHideControls`, `PlayerButtons`). Focus fill uses the accent token the way the
web client does (no permanent accent outlines).

## Parity list (minimum — the controls match the web player)

The **view** matches the web client; only the **streaming** matches the TV apps (that
part is mpv's job plus the capability profile, not the chrome's). Before building the
chrome, inventory every in-player element of **Vora.Web** `GlobalVideoPlayer`,
`Controls/` and `Panels/` (settings, subtitle tracks, Find Subtitles, Up Next) and write
the result to `Vora.Desktop/docs/player-parity.md` as a checklist. Anything the web
player has, the desktop player has. Panels that are mostly forms (Find Subtitles) may
pause playback and open in the web window instead of being redrawn in Lua — note
that choice in the checklist. At minimum:

- [ ] Header: title + episode line, back/exit
- [ ] Play/pause, ±seek, scrub bar with server scrub thumbnails and marker ticks
- [ ] Elapsed / remaining time
- [ ] Skip Intro / Recap / Preview / Credits chip + per-profile auto-skip
- [ ] Up-next card with countdown, cancel, play now; next/previous episode
- [ ] Settings panel: Quality (incl. the actual output, e.g. "4K HDR10 · Direct Play"),
      Audio tracks, Subtitles (incl. Off)
- [ ] Whatever stream details the web player shows (strategy, codecs, HDR, bitrate),
      with HDR-out taken from mpv's `video-out-params`
- [ ] Resume from the details VM position; Start over handled on the details page
- [ ] Extras/trailers play in the same player without touching the parent's watch state
- [ ] Buffering indicator; friendly error with "play with the built-in player" fallback
- [ ] Volume + mute (desktop-only addition)
- [ ] Desktop-only: windowed/fullscreen toggle
