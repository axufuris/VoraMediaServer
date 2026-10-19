# 01 — Architecture

## Processes

```
┌──────────────────────── Vora Desktop ────────────────────────┐
│                                                               │
│  Electron main process (TypeScript, Node)                     │
│   ├─ ServerStore        configured servers, trusted certs     │
│   ├─ WindowManager      BrowserWindow, navigation guard       │
│   ├─ MpvHost            spawns mpv, JSON IPC, restart/cleanup │
│   ├─ BridgeRouter       ipcMain handlers for the preload API  │
│   ├─ DesktopSettings    passthrough / HDR / hwdec, JSON file  │
│   └─ Updater            electron-updater, per-channel         │
│            ▲ ipc (contextBridge)            │ JSON IPC        │
│            │                                ▼ (pipe/socket)   │
│  Renderer: Vora.Web SPA            mpv process (own window)   │
│   loaded from https://<server>/     ├─ vo=gpu-next, HDR out    │
│   window.voraDesktop (preload)      └─ vora.lua chrome script │
│                                                               │
└───────────────────────────────────────────────────────────────┘
            │ HTTPS (same as a browser)        │ HTTPS range requests
            ▼                                  ▼
                       Vora.Api  (/api/streaming/play/{id}?t=…)
```

- The **renderer is the web client**, served by the Vora server. The shell never
  bundles a copy of Vora.Web. A server upgrade upgrades the desktop UI.
- **mpv is a separate process** with its own window, launched by `MpvHost`. It opens
  the signed `streamUrl` from `POST /api/streaming/start` directly — no proxying
  through Electron. The play token in the URL is the auth, so mpv needs no headers.
- The **Lua script** (`vora.lua`, shipped by the shell, loaded with `--script=`)
  draws the player chrome. It is a renderer of state plus a source of user intents.

## Who owns what

| Concern | Owner |
| --- | --- |
| Session start, track/part choice, pings, stop, resume, markers, auto-skip, up-next, Watch state | **Vora.Web** headless player controller (shared with the browser player) |
| Decoding, rendering, HDR, audio output/passthrough, local track switching | **mpv** |
| Drawing chrome, key/mouse handling during playback | **vora.lua** |
| Process lifecycle, IPC plumbing, sprite decoding, settings, window show/hide | **Electron main** |
| Capability decision (direct play vs transcode) | **Vora.Api** `BestPathDecisionManager` (unchanged philosophy: server decides, client reports) |

Rule: **no Vora business logic in the shell or in Lua.** If the browser player and
the desktop player would ever need to agree on something (when auto-skip fires, what
the up-next card shows, which subtitle is default), it lives in the shared controller.

## The engine abstraction (in Vora.Web)

`GlobalVideoPlayer` today owns both the logic and the `<video>` element. Doc 02 splits
it into:

```ts
interface PlaybackEngine {
  readonly kind: 'web' | 'desktop';
  load(source: EngineSource): Promise<void>;   // url, startPosition, strategy, tracks
  play(): void;
  pause(): void;
  seek(seconds: number): void;
  selectLocalTracks(sel: LocalTrackSelection): boolean; // false → controller restarts the session
  setChromeState(state: PlayerChromeState): void;      // no-op for the web engine
  stop(): Promise<void>;
  on<E extends keyof EngineEvents>(event: E, handler: EngineEvents[E]): () => void;
}

interface EngineEvents {
  time: (positionSeconds: number, durationSeconds: number) => void;
  paused: (isPaused: boolean) => void;
  buffering: (isBuffering: boolean) => void;
  ended: () => void;
  error: (error: EngineError) => void;
  intent: (intent: ChromeIntent) => void;   // desktop only: user acted in the mpv chrome
}
```

- `WebVideoEngine` wraps the current `<video>` + `loadHls` code. Behaviour for browser
  users must be byte-for-byte the same; the existing player tests must keep passing.
- `DesktopMpvEngine` forwards each call to `window.voraDesktop.player.*` and turns
  bridge events into `EngineEvents`.
- `usePlaybackController(engine, …)` is the extracted logic. `GlobalVideoPlayer`
  renders the web chrome from it; `DesktopPlaybackHost` renders **nothing** visible
  and instead pushes a `PlayerChromeState` to the engine on every change.

## The bridge contract

Exposed by the shell's preload via `contextBridge.exposeInMainWorld('voraDesktop', …)`.
The TypeScript declaration lives in **Vora.Web** (`src/desktop/desktopBridge.ts`), since
the web client is the consumer; the shell repo keeps an identical copy under
`src/shared/bridge.ts` and CI in `Vora.Desktop` diffs the two (doc 05).

```ts
interface VoraDesktopBridge {
  bridgeVersion: number;                    // starts at 1, bumped on any addition
  shellVersion: string;                     // app semver, shown in About/devices
  channel: 'qa' | 'prod';
  platform: 'windows' | 'linux';
  mpv: { available: boolean; version: string | null; reason: string | null };
  capabilities(): Promise<DesktopCapabilities>;
  player: {
    open(req: OpenRequest): Promise<void>;  // spawns/reuses mpv, loads url at start
    command(cmd: PlayerCommand): void;      // play | pause | seek | tracks | load-next
    setChromeState(state: PlayerChromeState): void;
    setScrubSprite(spec: ScrubSpriteSpec | null): Promise<void>;
    close(): Promise<void>;
    onEvent(handler: (e: PlayerEvent) => void): () => void;
  };
  settings: { get(): Promise<DesktopSettings>; set(patch: Partial<DesktopSettings>): Promise<DesktopSettings> };
  servers: { switchServer(): void };        // returns to the shell's connect page
}
```

Compatibility rules — the web client and shell ship independently (server upgrade vs
app update), so they **will** be mismatched in the field:

1. Additive only. Never rename or remove a member; add a new one and bump
   `bridgeVersion`.
2. Vora.Web feature-detects (`'setScrubSprite' in bridge.player`), never version-gates
   on equality.
3. If `window.voraDesktop` is missing, `mpv.available` is false, or `bridgeVersion` is
   below the minimum the web build needs, Vora.Web uses the web engine. The app must
   always play *something*.

## Playback sequence (happy path)

1. User hits Play. Controller calls `streamingService.startSession` with the
   **desktop capability profile** (doc 02) instead of the browser scan.
2. Server returns `strategy: "DirectPlay"` and `streamUrl: /api/streaming/play/{id}?t=…`.
3. Controller → `engine.load({ url: absolute(streamUrl), startPosition, … })` →
   bridge `player.open` → `MpvHost` starts mpv (or reuses the running one) and sends
   `loadfile <url> replace -1 start=<pos>`.
4. Electron hides its window (renderer keeps running — `backgroundThrottling: false`).
5. mpv emits `time-pos` / `pause` / `eof-reached` → main → renderer events → the
   controller pings the server exactly as the browser player does.
6. Controller computes chrome state (markers, skip chip, up-next…) and pushes it →
   main → `script-message-to vora state <json>` → Lua draws it.
7. User presses Skip Intro in mpv → Lua sends `script-message vora-intent {"type":"skip-marker"}`
   → main → renderer `intent` event → controller seeks. Same code path as the web chip.
8. Up-next: controller starts the next session and calls `engine.load` again; mpv
   stays open (no window flicker).
9. Exit: Lua `exit` intent (Back/Esc) → controller stops the session → bridge
   `player.close` → mpv quits → Electron window shows, focused, on the details page.

Non-DirectPlay strategies (remote over a slow link, user-forced quality) return an HLS
URL; mpv plays that too, and track changes fall back to a session restart, exactly
like the web client.

## What stays on the web engine inside the desktop app

- **Live TV / IPTV and timeshift.** Streams are H.264/AAC HLS that Chromium plays
  well, and the in-player guide is a large web surface. Revisit after v1.
- **Music, radio, podcasts.** Web Audio already provides EQ and crossfade.
- **Anything when mpv is unavailable** (Linux without mpv installed) — see doc 03.
