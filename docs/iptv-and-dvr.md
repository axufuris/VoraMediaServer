# IPTV & DVR

IPTV is treated as a first-class media source alongside the file library. Channels come from one or more **playlists** (M3U sources). Programme guide data comes from one or more **EPG sources** (XMLTV feeds) that are merged globally — see [`adr/0001-split-iptv-playlist-from-epg-source.md`](adr/0001-split-iptv-playlist-from-epg-source.md) for the rationale. The system also has favorites, blocked/hidden lists, recording sessions, and timeshift.

The same IPTV infrastructure carries **two presentation contexts**:

- **Live TV** (M3U playlists with `DefaultChannelKind = Tv`) — surfaced on the Live TV page, with EPG, DVR, and the program guide.
- **Internet Radio** (M3U playlists with `DefaultChannelKind = Radio`) — surfaced on the Audio hub's Radio tab, no EPG, no DVR.

Backend tables, the EPG cache, and the matching engine are shared. The split is purely presentational, driven by the `IptvChannelKind` discriminator on `IptvPlaylist.DefaultChannelKind` and `IptvChannel.Kind`. Admin endpoints accept `?kind=Tv|Radio` on `GET /iptv/admin/playlists` for filtered views.

## Backend endpoint groups

Each maps to one frontend service:

| Backend file | Mount | Frontend service |
| --- | --- | --- |
| `IptvClientEndpoints.cs` | `/api/iptv/client/...`, `/api/iptv/guide` | `api/Iptv/iptvClientService.ts` |
| `IptvAdminEndpoints.cs` | `/api/iptv/admin/playlists/...`, `/api/iptv/admin/epg-sources/...`, `/api/iptv/admin/channels/...` | `api/Iptv/iptvAdminService.ts`, `api/Iptv/iptvEpgAdminService.ts` |
| `DvrEndpoints.cs` | `/api/iptv/dvr/...` | `api/Iptv/dvrService.ts` |
| `DvrPlaybackEndpoints.cs` | `/api/streaming/dvr/play/...` | `api/Iptv/dvrPlaybackService.ts` |
| `TimeshiftEndpoints.cs` | `/api/iptv/timeshift/...` | `api/Iptv/timeshiftService.ts` |

Frontend services align 1:1 — don't lump DVR or timeshift methods back into `iptvClientService`. Playlists and EPG sources are independent aggregates, each with its own admin endpoint group and its own frontend service.

## Domain entities of interest

- **IPTV playlists** (`IptvPlaylist`) — admin-managed M3U sources. Each playlist has its own tuner profile (`IptvTunerProfile` with `MaxConcurrentStreams`), owns its `IptvChannel`s, and tracks `LastSyncedAt` (set on successful M3U fetch) and `LastError` (set on failure). Per-user visibility is filtered via `User.AllowedIptvPlaylistIds` / `UserProfile.AllowedIptvPlaylistIds`.
- **IPTV EPG sources** (`IptvEpgSource`) — standalone XMLTV feeds. No FK to a playlist. Carry `Priority` (lower = synced first), `IsActive`, `LastError`, `LastSyncedAt`.
- **IPTV channels** — discovered per playlist (`PlaylistId`). Admins can also hide channels server-wide via `IsHiddenByAdmin`.
- **EPG cache** — preloaded at startup (`StartupTaskExtensions`). Built by iterating every active EPG source in ascending `Priority` order. Each channel (`tvg-id`) is **claimed by the first source that covers it** — lower-priority sources only fill in channels the higher-priority sources don't cover. This prevents stacked/overlapping programmes when multiple feeds publish slightly different schedules for the same channel.
- Channel matching is layered, all case-insensitive: first an **exact** match against `IptvChannel.ExternalChannelId`; if that misses, the alphanumeric-only normalised form (drop dots, dashes, spaces); if a `tvg-id` ends in `@<quality>` (the iptv-org convention — `FastTV.us@SD`, `ComedyCentralEast.us@HD`), both the full form and the pre-`@` form are indexed so XMLTV feeds that omit the suffix still match. The matcher does **not** strip semantic words (`hd`/`tv`/`east`/etc.) — that historically caused collisions where many distinct channels collapsed to one.
- Quality variants in the M3U (`FastTV.us@SD` + `FastTV.us@HD`) both get matched by the same XMLTV channel ID (`FastTV.us`) — the same programmes land in both cache buckets.
- Use the **Match Diagnostics** modal on the IPTV admin page (`/api/iptv/admin/epg-diagnostics`) to inspect three things side by side: (1) channel coverage — how many DB channels have any EPG data and a sample of those without; (2) M3U `tvg-id` samples in the DB; (3) per-source stats — channels contributed, programmes matched/total, match rate, and a sample of XMLTV channel IDs that failed to match. The yellow-highlighted samples are the live truth — if you see a major channel sitting unmatched, compare the orange `tvg-id` column against the yellow XMLTV IDs to find the formatting gap.
- The XMLTV parser sets `DtdProcessing = DtdProcessing.Ignore` and `XmlResolver = null` — many real-world XMLTV feeds (`i.mjh.nz/*` etc.) declare a DOCTYPE that .NET's default `Prohibit` mode rejects.
- Programs are looked up via `GET /api/iptv/guide` with channel IDs and a time window.
- **Recording schedule** — links a channel + program (or series) to a future recording session, with optional `KeepMaxEpisodes`.
- **Recording session** — a single recording instance. Has status (`Pending`, `Recording`, `Completed`, `Completed (Partial)`, `Post-Processing`, `Failed`, `Conflict`, `Cancelled`), output file path, commercial markers JSON, file size, etc. See `IptvRecordingSessionVM`.

## DVR session statuses

The DVR Dashboard groups sessions into three tabs (`Completed`, `Upcoming`, `Failed`) by mapping statuses:

- Upcoming: `Pending`, `Recording`
- Completed: `Completed`, `Completed (Partial)`, `Post-Processing`
- Failed: `Failed`, `Conflict`, `Cancelled`

A `Post-Processing` recording is one that finished but hasn't been transcoded/scanned yet; playback is disabled until that completes.

## Series recording vs single

When the user records a series, the schedule has `isSeries = true` and an optional `keepMaxEpisodes` value (0 = keep all). The backend creates sessions for matching programs as the EPG predicts them, and cancels the oldest when retention is hit.

Cancelling a session that belongs to a series prompts the user: cancel this one episode (`deleteDvrSession`) or stop the whole series (`cancelDvrSeries`).

## Timeshift

Timeshift lets the user pause/rewind live TV. `POST /api/iptv/timeshift/start` returns a fresh HLS URL backed by a server-side buffer. `ping` keeps the buffer alive while watching, `stop` releases it.

**Permission gating in the client.** The Live TV skip-back/forward and record buttons are gated by `canTimeshiftIptv` / `canRecordLiveTv`. These are user-level (account-level) settings, edited in the admin `UserAccessModal`. Permissions are written into the profile JWT at sign-in, but they go stale after admin edits — so `LiveTvPlayer` calls `userService.getUserAccount(userId)` on mount and overwrites the JWT-derived values with the fresh server response. New player surfaces that gate on these claims should follow the same pattern.

## Per-profile preferences

Per-user / per-device IPTV state is stored under `ProfileDeviceIptvPrefs` (managed via `profileDeviceSettingsService`):

- Favorite channel IDs
- Hidden channel IDs
- Region filter (e.g. "US East")
- Resolution filter
- "Hide empty channels" toggle

This is what disappears if the `ClientDevice` row gets orphaned because the `X-Vora-Device-Id` header isn't matched correctly — see `docs/auth-and-devices.md`.

## Frontend pages

- `pages/Client/LiveTv/LiveTvPage.tsx` — main entry into Live TV.
- `pages/Client/LiveTv/LiveTvGuide.tsx` — the program grid (channels × time).
- `pages/Client/LiveTv/DvrDashboard.tsx` — recordings dashboard with the three tabs.
- `pages/Client/Audio/AudioHubPage.tsx` — Radio tab uses the same M3U + channels infrastructure, filtered to `Kind === 'Radio'`.
- `pages/Admin/Iptv/IptvPage.tsx` — shared admin page that takes a `kind: IptvChannelKind` prop. Mounted twice: `/admin/live-tv` (TV playlists + EPG sources + Match Diagnostics) and `/admin/internet-radio` (Radio playlists only). The "Radio Playlist" checkbox is gone — the active route determines kind.

`components/Iptv/GuideProgramModal.tsx` is the per-program record/play modal triggered from the guide. `components/Dvr/DvrSessionCard.tsx` is the recording card used in the dashboard. `components/Player/Panels/LiveTvRecordModal.tsx` is the same record dialog reached from the player.

## Player

Live TV uses **`components/Player/LiveTvPlayer.tsx`** instead of `GlobalVideoPlayer`. `MainLayout` picks which player to mount based on `currentMedia.playbackContextType === 'LiveTv'`. The two players share `Controls/` and `Panels/` primitives but have different chrome (channel up/down, CC toggle, embedded guide, etc.).
