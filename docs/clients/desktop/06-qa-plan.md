# 06 — QA plan

Run on the **QA build from GitHub Releases** (never a local build) before every prod
release. Record results in the PR or release notes as a filled-in copy of this table.

## Test machines

| Id | Machine | Display | Audio |
| --- | --- | --- | --- |
| W1 | Windows 11, NVIDIA or Intel GPU | HDR display, Windows "Use HDR" on | HDMI to an AV receiver (passthrough) |
| W2 | Windows 10 or 11 | SDR display | Speakers / headphones |
| L1 | Linux, KDE Plasma 6 on Wayland, recent mpv | HDR display, HDR on in Plasma | HDMI/receiver if available |
| L2 | Linux X11 session (any desktop), distro mpv | SDR | Speakers |
| L3 | Ubuntu LTS (24.04+): `.deb` install | any | any |
| L4 | Fedora: `.rpm` install | any | any |

## Test media (keep a small set on the QA server)

1. 4K HEVC 10-bit HDR10, MKV, TrueHD 7.1 Atmos + PGS subtitles
2. 4K Dolby Vision profile 8.1 (HDR10 base) MKV, E-AC-3 Atmos
3. 4K Dolby Vision profile 5 MP4 (no HDR10 fallback — wrong colours if mishandled)
4. HLG broadcast capture (TS)
5. 1080p H.264 MKV, DTS-HD MA 5.1, SRT sidecar + embedded ASS
6. AV1 10-bit WebM/MKV, Opus
7. VC-1 or MPEG-2 remux (M2TS)
8. TV season with analysed markers (intro + credits) and scrub thumbnails generated
9. A movie with extras/trailers
10. A multi-part (two-version) movie

## Install and update

- [ ] Windows installer: SmartScreen's "Windows protected your PC" → More info → Run anyway
      installs it; an update later installs with no prompt
- [ ] Prod and QA installed side by side; separate settings; separate Start-menu entries
- [ ] AppImage runs on L1/L2 (note any FUSE / sandbox steps); `.deb` on L3, `.rpm` on L4
      pull in mpv automatically
- [ ] Linux without mpv: the banner shows the right install command for that distro;
      video still plays in the built-in player; after installing mpv, focusing the
      window again switches to mpv with no restart
- [ ] QA auto-updates to the next QA build; prod ignores QA prereleases
- [ ] An update never interrupts playback; "Restart to update" works
- [ ] Uninstall removes the app; reinstall keeps the server list

## Connect and browse

- [ ] First run: connect page → server name shown → login → profile picker
- [ ] Self-signed HTTPS server: trust prompt shows the fingerprint; trusted on relaunch;
      a changed certificate prompts again
- [ ] Server offline: bundled error page, Retry works
- [ ] Switch server works; multi-server switching inside the SPA works
- [ ] External links open in the system browser; nothing else navigates the window
- [ ] Admin is visible for an admin and every admin page works in the window, same as
      a browser; a non-admin profile doesn't see it
- [ ] Devices / Now Playing show "Vora Desktop QA (Windows|Linux)" on a QA build ("Vora Desktop (…)" on prod), type Desktop
- [ ] Music, radio, podcasts, Live TV (incl. guide and timeshift) play in the web player

## Playback (run each medium on W1, W2, L1, L2)

- [ ] Media 1–7 Direct Play: the Info panel says Direct Play, server dashboard shows no transcode
- [ ] HDR display (W1, L1): real HDR output (`video-out-params` shows PQ/HLG; the display
      reports HDR mode). SDR display (W2, L2): tone-mapped, colours correct
- [ ] Media 3 (DV P5) colours correct on every machine
- [ ] PGS (media 1) shows with no transcode; ASS styling (media 5) correct; SRT sidecar loads
- [ ] Audio passthrough on W1 (Off / Core / All): the receiver shows TrueHD/Atmos,
      DTS-HD MA, E-AC-3 when set to All
- [ ] Audio and subtitle switching during Direct Play is instant (no session restart);
      after forcing a lower Quality it restarts and resumes at the same position
- [ ] Resume position correct (matches the details page); Start over works
- [ ] Watch state updates; Continue Watching correct after stopping part-way
- [ ] Remote server over a capped connection: the server transcodes to HLS and mpv plays it

## Player chrome (parity list in `04-player.md`)

- [ ] Every item on `Vora.Desktop/docs/player-parity.md` checked on W1 and L1; the
      controls look and behave like the web player side by side
- [ ] Skip Intro / Credits chips appear at the right times; auto-skip honours the profile setting
- [ ] Scrub thumbnails track the mouse and D-pad scrubbing; marker ticks show
- [ ] Up-next countdown → next episode with no window flicker; cancel works
- [ ] Keyboard-only and remote (D-pad + media keys) control works for a whole episode
- [ ] Chrome auto-hides after 3 s; mouse move shows it
- [ ] Chrome brightness comfortable on the HDR display
- [ ] Template colours (incl. a scheduled seasonal template) show in the chrome
- [ ] Exit returns to the details page with focus restored

## Failure handling

- [ ] Kill mpv mid-playback: the window comes back, the error is explained, the
      built-in player fallback works, and `mpv.log` has the details
- [ ] Network drop: buffering indicator, then recovery or a clear error
- [ ] Unsupported file (rename a text file to `.mkv` in a test library): friendly error
