# 03 — The Electron shell (`Vora.Desktop` repo)

## Stack

| Concern | Choice | Why |
| --- | --- | --- |
| Runtime | Electron, latest stable major at scaffold time; keep current (security fixes ship there) | Bundled Chromium = identical behaviour on Windows and every Linux distro (Tauri's WebKitGTK is the weak spot on Linux) |
| Language | TypeScript, `strict`, same lint rules as Vora.Web (no `any`, no `unknown` outside `catch`) | One standard across Andy's TS |
| Build | `electron-vite` (main / preload / renderer-for-local-pages) | Modern Vite-based pipeline, fast dev reload |
| Packaging + update | `electron-builder` + `electron-updater` | NSIS, AppImage, deb, rpm and GitHub-provider auto-update in one tool |
| Tests | `vitest` | Same as Vora.Web |
| Settings/state files | Plain JSON in `app.getPath('userData')`, written atomically via temp-file + rename in main | No dependency needed |
| Logging | Small file logger in main → `userData/logs/vora-desktop.log`, size-rotated (5 × 5 MB); mpv stderr goes to `mpv.log` beside it | Needed for user bug reports |

Before adding any other dependency, apply the "modern correct choice" check and ask.

## Repo layout

```
Vora.Desktop/
├─ CLAUDE.md                 rules + link to VoraMediaServer/docs/clients/desktop/
├─ package.json              version = next prod version (see doc 05)
├─ electron-builder.config.ts   one config, QA/prod differences via env
├─ electron.vite.config.ts
├─ src/
│  ├─ main/                  index.ts, windowManager.ts, serverStore.ts, certTrust.ts,
│  │                         mpvHost.ts, mpvIpc.ts, mpvDiscovery.ts, mpvConfig.ts,
│  │                         mpvPlayback.ts, mpvTracks.ts, capabilities.ts, spriteDecoder.ts,
│  │                         desktopSettings.ts, updater.ts, logger.ts
│  ├─ preload/index.ts       contextBridge → window.voraDesktop
│  ├─ shared/bridge.ts       copy of Vora.Web/src/desktop/desktopBridge.ts (CI-diffed)
│  ├─ pages/                 connect.html + error.html (local, bundled, themed)
│  └─ player/                vora.lua, mpv.conf, input.conf, fonts/
├─ resources/                icons (prod + QA variants), linux .desktop template
├─ scripts/                  mpv-windows.json (the pin) + fetch-mpv-windows.mts (npm run fetch-mpv)
└─ .github/workflows/        pr.yml, qa.yml, release.yml (doc 05)
```

## First run and servers

- **Connect page** (bundled `pages/connect.html`): server URL field, "Connect".
  Normalise (`https://` default, strip trailing `/`, accept `host:port`). Probe
  `GET /api/auth/setup-status`, the public endpoint the web login screen already uses;
  it returns `serverName`, shown before saving (do not add a new endpoint).
- `ServerStore` keeps `{ id, url, name, lastUsedAt }[]` plus the last-used id. On launch
  go straight to the last server.
- Load `https://<server>/` in the main window. Login, profile selection and
  multi-server switching inside the SPA all work as they do in a browser.
- **Switch server**: app menu item + `bridge.servers.switchServer()` → back to the
  connect page.
- **Server unreachable** on load → bundled `error.html` with Retry and Switch server.
  Do not show Chromium's error page.

## TLS for home servers

Many self-hosted servers use self-signed or LAN certificates.

- Handle `app.on('certificate-error')` for a saved Vora server's host only: show a
  bundled trust prompt with the certificate's SHA-256 fingerprint. On "Trust", pin the
  fingerprint for that host name in `certTrust.json` (the connect page's check only
  sees host names) and accept only that fingerprint afterwards (fingerprint change →
  prompt again, clearly worded).
- The connect page's check (`net.fetch` from main, which `certificate-error` doesn't
  cover) runs in its own in-memory session with `setCertificateVerifyProc`, replaced
  after a trust: Chromium caches a certificate check, and the same session would keep
  refusing a certificate that was trusted since.
- Never accept cert errors for any other host.
- mpv must see the same decision: when the server origin is pinned, launch mpv with
  `--tls-verify=no` **for that session only**; otherwise `--tls-verify=yes
  --tls-ca-file=<system bundle>` where mpv needs it. Verify mpv's default on the
  pinned builds rather than assuming.
- Plain `http://` LAN servers are allowed (same as a browser) — no prompt.

## Security (remote content in a privileged app)

The renderer runs code served by a network server, so lock it down:

- `BrowserWindow` `webPreferences`: `contextIsolation: true`, `sandbox: true`,
  `nodeIntegration: false`, `webviewTag: false`, `backgroundThrottling: false`
  (pings must keep running while the window is hidden during playback), `spellcheck: false`.
- The preload exposes `window.voraDesktop` only to the configured server's page (or
  the Vora.Web dev URL in dev) and `window.voraShell` only to the bundled pages. The
  main process decides per page from the sender frame; any other page gets nothing.
- `will-navigate` / `will-redirect`: allow the server origin; open anything else in
  the user's browser via `shell.openExternal` (https only); block the rest.
- `setWindowOpenHandler`: same rule; never create new Electron windows from content.
- Every `ipcMain.handle` validates its payload shape and checks `event.senderFrame.url`
  is the server origin (a bundled page, for the connect and error pages' calls). Treat
  renderer input as untrusted (URLs passed to mpv must be
  `http(s)` on the server origin — mpv must never be told to open a local path or a
  third-party URL from the renderer).
- Deny all permission requests (`setPermissionRequestHandler`) except fullscreen and
  clipboard-sanitised-write.
- DevTools: enabled in QA builds, disabled in prod (Ctrl+Shift+I does nothing).
- Electron Fuses at package time: `RunAsNode` off, `EnableNodeCliInspectArguments` off,
  `EnableEmbeddedAsarIntegrityValidation` on, `OnlyLoadAppFromAsar` on.

## Windows and app behaviour

- Single instance (`requestSingleInstanceLock`); a second launch focuses the window.
- Remember window size/position/maximised; start maximised on first run.
- Minimal menu: Switch server · Reload · Toggle fullscreen (F11) · About · (QA) DevTools.
  On Linux keep the menu bar hidden (Alt shows it); on Windows the same.
- Linux: Electron 38.2 and later pick Wayland where it's available and X11 otherwise
  by default, so no ozone switch is set.
- When mpv loads the first file of a run, hide the main window; restore and focus it
  when mpv exits. If mpv dies on its own, the page gets an `error` event ("stopped
  before the video started" or "stopped unexpectedly") and then `closed`, and offers
  the web-player fallback. A user quitting mpv (`q`) gives only `closed`, which the web
  client treats as Back.

## mpv discovery (`mpvDiscovery.ts`)

| Platform | Where mpv comes from | Minimum |
| --- | --- | --- |
| Windows | **Bundled** in the installer under `resources/mpv/` (pinned build, see doc 05) | Whatever we pin |
| Linux | **System mpv** from the user's package manager (`mpv` on `PATH`, plus `/usr/bin/mpv`, `/usr/local/bin/mpv`). The .deb/.rpm declare it as a dependency; the AppImage cannot | **0.37+** (`MINIMUM_MPV`), so Ubuntu 24.04 LTS's mpv plays; nothing the shell sends needs newer (it sets `start` instead of using `loadfile`'s 0.38 index argument), and CI plays a file in that version. Recommend the newest the distro has — HDR output on Wayland needs a recent mpv + compositor |

- Parse `mpv --no-config --version`. Also require Lua (a probe script that logs a
  marker and quits, run with `--script=`) and the `gpu-next` VO (`--vo=help`). Each
  check is a short process (about 80 ms); they run in parallel at startup, and the
  server page loads after them so the page never sees a passing "missing" state.
- Result → `bridge.mpv = { available, version, reason }`. When unavailable, `reason` is
  the whole explanation the banner shows: missing ("…needs mpv, which isn't installed.
  Install it with `sudo apt install mpv`.", the command from `/etc/os-release` `ID` /
  `ID_LIKE`: debian/ubuntu → apt, fedora/rhel → dnf, arch → pacman, opensuse → zypper,
  otherwise "Install mpv from your distribution."), too old, no Lua, or no gpu-next.
- Re-run discovery when the window regains focus while mpv is unavailable (at most
  every 5 s), so installing mpv doesn't need an app restart, and send a change through
  `onMpvChanged` (the page's `bridge.mpv` is a copy taken at load).

## Capabilities (`capabilities.ts`)

`bridge.capabilities()` returns the codec/container lists the web profile reports
(doc 02 D). Derive them from the actual mpv binary rather than hard-coding: run
`mpv --vd=help` and `mpv --ad=help` during discovery (a line `dca (dts) - …` is a
decoder for the codec in brackets), intersect with the list Vora's decision manager
knows, and keep the result with the binary. Containers: mpv/FFmpeg demux all the
listed ones; hard-code them. `maxAudioChannels: 8`.

## Desktop settings (`desktopSettings.ts`)

```ts
interface DesktopSettings {
  useMpvForVideo: boolean;               // default true
  audioPassthrough: 'off' | 'core' | 'all'; // core = ac3,dts ; all = + eac3,truehd,dts-hd
  hdrOutput: boolean;                    // default true → target-colorspace-hint
  hardwareDecoding: 'auto' | 'off';      // auto → hwdec=auto-safe
}
```

Persisted in `userData/settings.json`. Changes apply on the next `player.open`.
The "advanced mpv settings" folder is `userData/mpv/` containing a user-editable
`mpv.conf` included **after** Vora's defaults (doc 04), created empty with a header
comment explaining it. `settings.openAdvancedFolder()` opens it with `shell.openPath`.

## Dev workflow (Visual Studio 2026 / CLI)

- Open `D:\Development\Vora.Desktop` with **Open Folder**; npm scripts:
  `dev`, `build`, `test`, `lint`, `typecheck`, `package:win`, `package:linux`.
- `npm run dev` with `VORA_DESKTOP_DEV_URL=http://localhost:5173` loads the Vite dev
  server of Vora.Web (API in Docker as usual), so web + shell changes hot-reload together.
- On Windows dev machines, `npm run fetch-mpv` populates `resources/mpv/` (git-ignored)
  so dev playback uses the same mpv as the installer.
- In a VS Code terminal, unset `ELECTRON_RUN_AS_NODE` first: VS Code sets it for its
  own processes, and with it Electron starts as plain Node.
- Linux testing: the Linux machine installs the QA AppImage/deb from GitHub (doc 05).
