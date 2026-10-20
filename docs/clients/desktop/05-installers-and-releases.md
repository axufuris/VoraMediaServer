# 05 — Installers, signing, auto-update, QA + prod releases

Releases live on **`axufuris/Vora.Desktop` → GitHub Releases** (public repo, so anyone
can download). The flow mirrors the server's: **QA on every push to `main`**, **Prod on
a published GitHub Release**.

## Two app identities

QA and Prod are different apps so testers can run both side by side and neither
updates into the other.

| | Prod | QA |
| --- | --- | --- |
| `appId` | `net.getvora.desktop` | `net.getvora.desktop.qa` |
| `productName` (userData folder, window title, Start menu) | `Vora` | `Vora QA` |
| Linux package / executable | `vora-desktop` / `vora-desktop` | `vora-desktop-qa` / `vora-desktop-qa` |
| Icon | brand icon | brand icon with a "QA" badge (`resources/icons/qa/`) |
| `bridge.channel` / `X-Vora-Client` | `prod` / `Vora Desktop` | `qa` / `Vora Desktop QA` |
| Version | `X.Y.Z` | `X.Y.Z-qa.<run_number>` |
| Update feed | non-prerelease releases | QA prereleases only |
| DevTools | off | on |
| Release type on GitHub | Release | Pre-release |

One `electron-builder.config.ts` reads `VORA_CHANNEL=qa|prod` and switches these
fields. Nothing else differs, so QA really tests what prod ships.

## Artifacts (x64)

| Platform | Format | Prod file name | Auto-update |
| --- | --- | --- | --- |
| Windows 10/11 | NSIS installer, per-user (no admin), `oneClick` | `Vora-Setup-X.Y.Z.exe` | yes |
| Linux (any distro) | AppImage | `Vora-X.Y.Z.AppImage` | yes |
| Debian / Ubuntu / Mint | `.deb` (`Depends: mpv`) | `vora-desktop_X.Y.Z_amd64.deb` | notify → download page |
| Fedora / openSUSE / RHEL | `.rpm` (`Requires: mpv`) | `vora-desktop-X.Y.Z.x86_64.rpm` | notify → download page |
| all | checksums | `SHA256SUMS.txt` | — |

QA names insert `-QA` (`Vora-QA-Setup-X.Y.Z-qa.57.exe`, `vora-desktop-qa_…`).

Linux notes the CLI must verify and document in the release notes template:

- Build Linux artifacts on **`ubuntu-22.04`** (older glibc → the AppImage runs on more
  distros). Install `rpm` on the runner for the `.rpm` target.
- The AppImage needs FUSE 2 on some distros (`libfuse2` / `libfuse2t64` on Ubuntu).
- Ubuntu 24.04+ restricts unprivileged user namespaces, which can stop Electron's
  sandbox from starting in an AppImage. Test it. **Do not** ship `--no-sandbox` to work
  around it — this app runs remote content. Recommend the `.deb` on Ubuntu and
  document the AppImage limitation if it is real.
- The `.deb`/`.rpm` must keep `chrome-sandbox` set up the way electron-builder does it.
- Ship a `.desktop` entry with the right `StartupWMClass` so the dock icon groups.

## Windows: bundled mpv

- `scripts/mpv-windows.json` pins the mpv build: mpv's own tagged release from
  `mpv-player/mpv` (`mpv-vX.Y.Z-x86_64-w64-mingw32.zip`, generic x86_64, Lua and
  gpu-next included, no 7-Zip needed), file name, URL, **SHA-256**, source tag.
- `scripts/fetch-mpv-windows.mts` (`npm run fetch-mpv`) downloads, verifies the hash
  (fails on mismatch), unpacks the zip and the zip inside it, and copies `mpv.exe` with
  its DLLs to `resources/mpv/`. `extraResources` copies it into the installer.
- Licensing: mpv is GPL-2.0+ (FFmpeg components LGPL/GPL); Vora is AGPL-3.0. The zip
  has no licence files, so fetch mpv's `LICENSE.GPL`, `LICENSE.LGPL` and `Copyright`
  from the same tag, and ship them with a `THIRD-PARTY-NOTICES.md` naming the exact upstream
  release/commit with a link to its source. Flag anything unclear to Andy rather than
  guessing; this is not legal advice.
- Updating mpv = change the pin in a PR; QA tests it like any other change.

## Windows code signing

Unsigned installers trigger SmartScreen ("Windows protected your PC").

- Use **Azure Artifact Signing** (formerly Trusted Signing) through electron-builder's
  `win.azureSignOptions`. Andy already knows Azure. Check current pricing and whether
  individual developers are eligible before setting it up; tell Andy what you found.
- Secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, plus the
  endpoint, account name and certificate profile as repo **variables**.
- **Prod fails** if signing isn't configured. **QA signs if configured**, otherwise
  builds unsigned with a `::warning::`.
- Linux isn't signed. Publish `SHA256SUMS.txt` (GPG-signed checksums are a later
  follow-up).

## Auto-update (`updater.ts`)

- `electron-updater` with the GitHub provider (`owner: axufuris`, `repo: Vora.Desktop`).
- Prod: `allowPrerelease = false`, default channel (`latest.yml`, `latest-linux.yml`).
- QA: `channel = 'qa'`, `allowPrerelease = true` (`qa.yml`, `qa-linux.yml`). Check that
  electron-builder emits channel files for a `-qa.N` prerelease. If it only knows
  `alpha`/`beta`, use `beta` as the internal channel and keep "QA" everywhere users see it.
- Check on launch and every 6 h; download in the background; when ready, show a
  non-blocking "Restart to update" notice. **Never restart or prompt during playback.**
- `.deb`/`.rpm` installs can't self-update: show "Version X is available" with a button
  that opens the release page.
- Upload every `*.yml` and `*.blockmap` electron-builder produces, or updates break.

## Versioning

- `package.json` `version` = the **next** prod version (e.g. `1.1.0`).
- QA build: CI sets `X.Y.Z-qa.${{ github.run_number }}` in the runner checkout
  (`npm version --no-git-tag-version`); nothing is committed.
- Prod: the release tag must be exactly `v` + `package.json` version, or the workflow fails.
- After a prod release, a PR bumps `package.json` to the next version.

## Workflows (`.github/workflows/`)

**`pr.yml`** — on `pull_request`:
1. `npm ci`, `lint`, `typecheck`, `test`.
2. **Bridge contract check**: check out `axufuris/VoraMediaServer` (if that repo is
   private, use a read-only fine-grained token secret `VORA_SERVER_READ_TOKEN`) and
   diff `src/Vora.Web/src/desktop/desktopBridge.ts` against `src/shared/bridge.ts`.
   Fail on any difference.
3. Matrix `windows-latest` + `ubuntu-22.04`: package with `--publish never` to prove the
   installers build. Upload them as workflow artifacts (7-day retention).

**`qa.yml`** — on `push` to `main` + `workflow_dispatch`; `concurrency: qa`,
`cancel-in-progress: true`:
1. Test job (same as PR).
2. Build matrix with `VORA_CHANNEL=qa`, version `X.Y.Z-qa.N`, `--publish never`.
3. Publish job (`permissions: contents: write`): download all artifacts, write
   `SHA256SUMS.txt`, then
   `gh release create vX.Y.Z-qa.N --prerelease --target $GITHUB_SHA --title "Vora Desktop QA X.Y.Z-qa.N" --notes-file notes.md`
   (notes = the commit subjects since the previous QA tag) and upload every file.
4. Prune: keep the 10 newest QA prereleases; delete older ones with `--cleanup-tag`.

**`release.yml`** — on `release: published` + `workflow_dispatch` (input: tag):
1. Skip if `github.event.release.prerelease` or the tag contains `-qa.`.
2. Check out the tag; fail if it doesn't match `package.json`.
3. Test job.
4. Build matrix with `VORA_CHANNEL=prod`, signing on, `--publish never`.
5. `SHA256SUMS.txt`, then `gh release upload <tag> <files> --clobber`.

Why `--publish never` + `gh`: electron-builder won't upload to a release that's already
published. Uploading explicitly also makes QA and prod use the same path.

Use the action major versions the server's workflows use (`actions/checkout@v5`,
`upload-artifact@v6`, …) and their habits: `workflow_dispatch` on every workflow,
separate cache scopes per channel, and explanatory YAML comments where a step isn't
obvious (YAML comments are fine; the no-comments rule is for C#).

## Secrets and variables

| Name | Kind | Used by |
| --- | --- | --- |
| `GITHUB_TOKEN` | built-in | creating/uploading releases |
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | secret | Windows signing |
| `AZURE_SIGNING_ENDPOINT`, `AZURE_SIGNING_ACCOUNT`, `AZURE_SIGNING_PROFILE` | variable | Windows signing |
| `VORA_SERVER_READ_TOKEN` | secret, only if VoraMediaServer is private | bridge contract check |

Write `Vora.Desktop/.github/RELEASING.md` covering these and the checklist below, the
way `Vora.Android/.github/RELEASE_SIGNING.md` does for Android.

## Andy's release checklist (put it in `RELEASING.md`)

1. Merge to `main` → a QA prerelease appears within ~20 min; QA builds auto-update.
2. Run `06-qa-plan.md` on the QA build (Windows + the Linux box).
3. Create a GitHub Release `vX.Y.Z` on `main` with release notes, and publish it.
4. `release.yml` attaches the installers (~20 min). The release has no downloads until
   it finishes.
5. Smoke-test the prod installer; prod users auto-update.
6. Update the download links on getvora.net (`vora-site` repo) if they point at
   specific files (prefer `releases/latest`).
7. PR: bump `package.json` to the next version.

## Follow-ups (not v1)

Flathub (best long-term Linux channel, and it could ship mpv as part of the app), winget
manifest, Windows/Linux `arm64` matrix entries, GPG-signed checksums.
