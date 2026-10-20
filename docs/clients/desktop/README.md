# Vora Desktop — build plan (start here)

This folder is the hand-off for building **Vora Desktop**, the Windows + Linux client.
Read this file first, then the numbered docs in order. Each doc is ≤200 lines and
self-contained enough to load on its own while working on that part.

| Doc | What it covers |
| --- | --- |
| [`01-architecture.md`](01-architecture.md) | Process model, data flow, the shell ↔ web bridge contract, the engine abstraction |
| [`02-server-and-web.md`](02-server-and-web.md) | Every change in **this** repo (`Vora.Api` + `Vora.Web`) |
| [`03-shell.md`](03-shell.md) | The Electron shell in the new `Vora.Desktop` repo |
| [`04-player.md`](04-player.md) | mpv playback, the Lua player chrome, web-player parity list |
| [`05-installers-and-releases.md`](05-installers-and-releases.md) | Installers, auto-update, QA + prod GitHub releases |
| [`06-qa-plan.md`](06-qa-plan.md) | The test matrix a build must pass before a prod release |

## The decision

Vora Desktop is **not** a new UI. The app **looks and behaves like the web client**,
including the Admin area for admins. What it matches the TV apps on is **streaming**:
what plays without a transcode. It is:

1. **An Electron shell** that loads the Vora.Web SPA **from the user's Vora server**
   (the same SPA the server already hosts). Browsing, settings, admin, music, Live TV
   and everything else is the web client, unchanged. Do not hide or trim admin pages.
2. **mpv for video playback.** When the user plays a movie, episode, extra or DVR
   recording, mpv takes over in its own fullscreen window and direct-plays the
   original file (HEVC, AV1, HDR10/HLG/Dolby Vision, TrueHD/DTS-HD/Atmos, PGS…).
3. **The web player's controls, redrawn as an mpv script** (Lua), drawn by mpv inside
   its own output so HDR is preserved. It copies the look and controls of Vora.Web's
   `GlobalVideoPlayer`, not the TV apps'. It renders state; it holds no business logic.
4. **All player logic stays in Vora.Web.** The web player's session/ping/marker/
   auto-skip/up-next/track logic is extracted into a headless controller that drives
   either the existing `<video>`+hls.js engine or the desktop mpv engine.

Why this and not a native or Compose/Avalonia desktop app: the desktop UI is the web
UI (mouse + keyboard), the only thing a native app adds is playback, and mpv is the
best player on both OSes — a native app would end up embedding mpv/libVLC anyway.
This keeps maintenance to **one more player chrome**, not one more client.

This is a deliberate exception to "native per ecosystem" (the Apple / Android / Roku
strategy). That strategy exists for TV focus engines and platform players; neither
applies on desktop.

## Hard requirements (do not trade these away)

- **Direct play** of HEVC, AV1, VP9, H.264, MPEG-2/VC-1; TrueHD, DTS / DTS-HD MA,
  E-AC-3 / Atmos, AC-3, FLAC, PCM; MKV, MP4, M2TS/TS, AVI, WebM. The server must not
  transcode for the desktop client unless bandwidth or the user forces it.
- **Real HDR output** (HDR10/HLG) on HDR displays; good tone-mapping on SDR displays.
  Nothing composites the video through Chromium.
- **Audio passthrough** to a receiver (opt-in setting), including TrueHD and DTS-HD.
- **Streams like the TV apps, not like a browser**: the shell probes what mpv and the
  machine can play (codecs, containers, HDR, audio output) and reports it the same way
  the other clients report their device, and the server's three streaming profiles
  decide as they already do.
- **The web client's look and controls**, admin included. The player controls match
  the web player — see the parity list in `04-player.md`.
- **Windows (x64) and Linux (x64)** from one codebase, one CI pipeline.
- **QA and Prod builds**, installable side by side, downloadable from GitHub Releases,
  each auto-updating only within its own channel.

## Repos

| Repo | Role |
| --- | --- |
| `VoraMediaServer` (this repo) | Capability + bridge changes in `Vora.Api` and `Vora.Web` (doc 02) |
| `Vora.Desktop` (new, `D:\Development\Vora.Desktop`, public GitHub repo `axufuris/Vora.Desktop`, AGPL-3.0 like the server) | Electron shell, mpv integration, Lua chrome, installers, release workflows (docs 03–05) |

`Vora.Desktop` stays out of `Vora.slnx`, like the other clients. Its own `CLAUDE.md`
must link back to this folder as the design source of truth rather than copying it.

## Build order

Work in this order; each phase ends in a working, committed state on a feature branch
cut from an up-to-date `main`, and a PR (no work on `main` in either repo).

1. **Server + web groundwork** (doc 02). Ship it first: it is harmless to browsers
   and every later phase depends on it.
2. **Shell skeleton** (doc 03): connect-to-server page, loading the SPA, security
   hardening, the bridge with `player.open` stubbed. At this point the desktop app is a
   working Vora web client in a window, with the web player.
3. **mpv engine** (doc 04, first half): spawn/IPC, direct play, resume, pings,
   track switching, end-of-file, errors + fallback to the web player.
4. **Lua chrome** (doc 04, second half): full parity list.
5. **Installers + CI** (doc 05): PR checks, QA prereleases on every push to `main`,
   prod releases on a published GitHub Release.
6. **QA pass** (doc 06) on real hardware, then the first prod release `v1.0.0`.

Phases 2 and 5 can overlap — getting the QA pipeline up early means every later
phase is testable on the Linux box that asked for this.

## Out of scope for v1

- macOS (Apple is covered by the planned Swift client; revisit only if asked).
- Windows/Linux on ARM (add an `arm64` matrix entry later; nothing here blocks it).
- Flatpak / Flathub, Snap, winget, Microsoft Store (good follow-ups; see doc 05).
- A desktop-specific UI. The web client (admin included) is the UI.
- Offline downloads.

## Done means

- A Linux user can install from the GitHub release, connect to a server, and direct
  play a 4K HDR HEVC + TrueHD MKV with HDR output on KDE Plasma (Wayland) and with
  tone-mapping on X11, with controls that look like the web player's.
- A Windows user gets the same, with HDR output and optional TrueHD/DTS-HD passthrough.
- Every item in the `04-player.md` parity list works.
- Browser users of Vora.Web see no behaviour change.
- QA and Prod installers come out of CI with no manual build steps.
