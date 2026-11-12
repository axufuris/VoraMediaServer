# IPTV & DVR

IPTV is treated as a first-class media source alongside the file library. Channels come from one or more **playlists** (M3U sources). Programme guide data comes from one or more **EPG sources** (XMLTV feeds) that are merged globally. Playlists and EPG sources are **independent aggregates** — an EPG source has no FK to a playlist, because the useful XMLTV bundles (`epg_ripper_*` and friends) carry `tvg-id`s spanning many playlists and have no natural parent. An EPG feed that could only belong to one playlist was the original bug: EPG-only rows owned no channels, so every programme they parsed was discarded. The system also has favorites, blocked/hidden lists, recording sessions, and timeshift.

The same IPTV infrastructure carries **two presentation contexts**:

- **Live TV** (M3U playlists with `DefaultChannelKind = Tv`) — surfaced on the Live TV page, with EPG, DVR, and the program guide.
- **Internet Radio** (M3U playlists with `DefaultChannelKind = Radio`) — surfaced on the Audio hub's Radio tab, no EPG, no DVR.

Backend tables, the EPG cache, and the matching engine are shared. The split is purely presentational, driven by the `IptvChannelKind` discriminator on `IptvPlaylist.DefaultChannelKind` and `IptvChannel.Kind`. Admin endpoints accept `?kind=Tv|Radio` on `GET /iptv/admin/playlists` for filtered views.

### Both features are reported as "on AND usable"

`FeatureFlagsVM.LiveTv` / `.InternetRadio` are `ServerSetting.Enable*` **and** `IIptvRepository.GetSourceAvailabilityAsync`, which answers both kinds in one pass: a non-hidden channel of that kind on an active playlist, else an active playlist carrying an M3U URL (or, for an Xtream account, a server address) whose `DefaultChannelKind` matches (so a source counts before its first sync). Web, Android and Roku all gate their navigation on these, and an entry that opens an empty page is worse than no entry. Channels are matched on their **own** `Kind`, not the playlist's default, because that is what the clients render — `useGuideData` and `RadioTab` both filter per channel.

`Discover` derives the same way, from `IDiscoveryManager.IsAvailableAsync` — every row on that page comes from an `IDiscoveryProvider`, and one with no key returns none, so an unconfigured server's Discover page is blank. `ReleaseCalendar` deliberately does **not**: its providers include `LocalCalendarProvider`, which builds events out of the library and needs no configuration, so deriving it from the Radarr/Sonarr request servers would hide a working calendar from every server that doesn't run them.

The derivation is **read-side only**. `UpdateFeatureFlagsAsync` still writes exactly what the admin sent, and `FeatureFlagsVM` carries `LiveTvEnabled` / `InternetRadioEnabled` / `DiscoverEnabled` — the raw stored toggles — for the admin switches. Both halves matter: a switch bound to the derived value reads off on a server with no playlist and looks broken when flipped, and a save that echoed a derived `false` back would erase the admin's choice the moment a source stopped resolving. `featureFlagsService.updateFeatureFlags` converts to the request shape in one place so no caller can PUT a derived value.

`IX_IptvChannels_PlaylistId_Kind` exists for this: the flags endpoint runs on every client's nav load, and without it the *negative* case (no radio channels among 50k TV rows) is a full scan every time.

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

- **IPTV playlists** (`IptvPlaylist`) — admin-managed sources: an M3U address or an Xtream Codes account (`SourceType`, see Providers that need an account below). Each playlist has its own tuner profile (`IptvTunerProfile` with `MaxConcurrentStreams`), owns its `IptvChannel`s, and tracks `LastSyncedAt` (set on a successful load) and `LastError` (set on failure, redacted, admin-only). Per-user visibility is filtered via `User.AllowedIptvPlaylistIds` / `UserProfile.AllowedIptvPlaylistIds`, always through `IptvPlaylistAccess` (account and profile).
- **The nightly IPTV job** (`ServerSetting.IptvSyncTime`, `QueueIptvScheduledSync`) reloads the channel list of every active playlist, then syncs the guide and turns DVR schedules into sessions. Lineups and providers' stream tokens change, so a playlist added once does not stay right on its own.
- **IPTV EPG sources** (`IptvEpgSource`) — standalone XMLTV feeds. No FK to a playlist. Carry `Priority` (lower = synced first), `IsActive`, `LastError`, `LastSyncedAt`.
- **IPTV channels** — discovered per playlist (`PlaylistId`). Admins can also hide channels server-wide via `IsHiddenByAdmin`.
- **EPG cache** — preloaded at startup (`StartupTaskExtensions`). Built by iterating every active EPG source in ascending `Priority` order. Each channel (`tvg-id`) is **claimed by the first source that covers it** — lower-priority sources only fill in channels the higher-priority sources don't cover. This prevents stacked/overlapping programmes when multiple feeds publish slightly different schedules for the same channel.
- Channel matching is layered, all case-insensitive: first an **exact** match against `IptvChannel.ExternalChannelId`; if that misses, the alphanumeric-only normalised form (drop dots, dashes, spaces); if a `tvg-id` ends in `@<quality>` (the iptv-org convention — `FastTV.us@SD`, `ComedyCentralEast.us@HD`), both the full form and the pre-`@` form are indexed so XMLTV feeds that omit the suffix still match. The matcher does **not** strip semantic words (`hd`/`tv`/`east`/etc.) — that historically caused collisions where many distinct channels collapsed to one.
- Quality variants in the M3U (`FastTV.us@SD` + `FastTV.us@HD`) both get matched by the same XMLTV channel ID (`FastTV.us`) — the same programmes land in both cache buckets.
- Use the **Match Diagnostics** modal on the IPTV admin page (`/api/iptv/admin/epg-diagnostics`) to inspect three things side by side: (1) channel coverage — how many DB channels have any EPG data and a sample of those without; (2) M3U `tvg-id` samples in the DB; (3) per-source stats — channels contributed, programmes matched/total, match rate, and a sample of XMLTV channel IDs that failed to match. The yellow-highlighted samples are the live truth — if you see a major channel sitting unmatched, compare the orange `tvg-id` column against the yellow XMLTV IDs to find the formatting gap.
- The XMLTV parser sets `DtdProcessing = DtdProcessing.Ignore` and `XmlResolver = null` — many real-world XMLTV feeds (`i.mjh.nz/*` etc.) declare a DOCTYPE that .NET's default `Prohibit` mode rejects.
- Adding, updating or deleting an `IptvEpgSource` **awaits** the sync before returning, so the response carries a fresh `LastSyncedAt`. Fire-and-forget left the UI showing `null` until the admin refreshed by hand.
- A stale `LastSyncedAt` next to a non-null `LastError` reads as "last good sync was at X, broken since" — failures preserve the previous timestamp rather than clearing it.
- Programs are looked up via `GET /api/iptv/guide` with channel IDs and a time window.
- **Recording schedule** — links a channel + program (or series) to a future recording session, with optional `KeepMaxEpisodes`.
- **Recording session** — a single recording instance. Has status (`IptvRecordingSessionStatus`: `Pending`, `Recording`, `PostProcessing`, `DetectingCommercials`, `Completed`, `Failed`, `Conflict`, `Cancelled`, sent as those names), output file path, commercial markers JSON, file size, etc. See `IptvRecordingSessionVM`.

## Providers that need an account

Paid providers sell an **Xtream Codes account** (server address, username, password) or an M3U address that only works with a login or with particular request headers. All of it stays on the server.

**What is stored, and where.** `IptvPlaylist.SourceType` is `M3u` (default) or `Xtream`. An account keeps `ServerUrl` (normalised: `http://` added, `/player_api.php` and friends removed), `Username` and `PasswordCiphertext`. An M3U playlist can also keep `Username` / `PasswordCiphertext`, sent as HTTP Basic auth, and both kinds can keep `UserAgent` and `Referer`. The password is sealed with DataProtection (`IIptvSecretProtector`, purpose `Vora.Iptv.ProviderPassword.v1`, the same key ring as the SMTP password, so mount `StoragePaths:DataProtection`). It is write-only in the API: `NewPassword` / `ClearPassword` in, `PasswordIsSet` out, the SMTP pattern. A password the key ring can no longer open (keys lost, or a restore onto another server) gives the readable error "Enter the password again"; nothing is decrypted anywhere else. Never give one of these a default value in the entity (`docs/database.md`).

**Xtream ingest** (`IptvChannelLoader`, `XtreamParser`). `player_api.php` first: `auth: 0` is "The provider rejected the username or password", a status other than Active or an `exp_date` in the past is named in `LastError`, and none of them carries an address. Status, expiry and `max_connections` are kept on the playlist (`AccountStatus`, `AccountExpiresAt`, `AccountMaxConnections`, `AccountCheckedAt`) and shown on the admin page. Then `get_live_categories` (group titles) and `get_live_streams`: `stream_icon` is the logo, `epg_channel_id` the `ExternalChannelId`. A guide id shared by several streams stays on the lowest `stream_id`; the others become `{id}@{stream_id}`, which the guide matcher already strips (`@` suffixes, above), so every variant gets the guide and the `(PlaylistId, ExternalChannelId)` index holds. A stream without a guide id is `xtream-{stream_id}`. `stream_type: radio_streams` is a radio channel.

**Channels hold no credentials.** An Xtream channel's `StreamUrl` is `xtream:{stream_id}.{ext}`; `IptvUpstreamResolver.Resolve` turns it into `{server}/live/{user}/{pass}/{id}.{ext}` only when a stream is opened, so changing the password needs no reload. The extension is `m3u8` when the account's `allowed_output_formats` include it (passthrough only proxies HLS for video), `ts` otherwise.

**One request policy for every outbound call** (`IptvRequestPolicy`, from `IIptvUpstreamResolver.PolicyFor`). Headers are the channel's own, else the playlist's, else the old defaults (the IPTV client's browser agent; `WinampMPEG/5.0` for radio audio). Basic auth goes only to the M3U address's host and port; .NET drops it on redirects. It covers the M3U and Xtream downloads, the provider guide, health probes, passthrough sniffing, playlists, segments and audio, and the timeshift and DVR ffmpeg runs, which get `-user_agent` and `-headers "Referer: …"` before `-i`; an M3U login reaches ffmpeg as userinfo in the address, and only for streams on the playlist host (ffmpeg answers the 401 challenge and does not carry it across a redirect). M3U channels read `#EXTVLCOPT:http-user-agent` / `http-referrer`, `#EXTHTTP` JSON, `#KODIPROP:inputstream.adaptive.stream_headers` and the Kodi `url|User-Agent=…&Referer=…` suffix, which is taken off the stored address (the fallback id is still hashed from the line as written, so ids don't move).

**What clients see.** Client view models (`GET /api/iptv/client/playlists/{userId}` and smart-list channel entries, `IptvPlaylistView.ForClient`) never carry the address of an authenticated playlist: Xtream, an M3U with a login or headers, or an M3U address with userinfo or credential-like query values. For those, `M3uUrl` is scheme and host only (`http://provider.example.com`, never blank, so apps still list the provider) and every `StreamUrl` is `vora-channel:{channelId}` (unique, so apps that dedupe on it still work). Free playlists keep their real addresses, which older Android and Roku builds play radio from, except a channel whose own address carries credentials. `LastError`, `Username`, `ServerUrl`, the headers and the account fields are never sent to clients. Live TV and radio play only through timeshift or passthrough, so nothing a client plays needs the address.

**Access.** The client playlists endpoint and `POST /api/iptv/guide` answer for the caller's own account and profile, whatever `userId` / `profileId` the request names (an admin may still ask about another account), and an unknown account gets nothing. Timeshift start, DVR scheduling and passthrough start all check the account and profile allowlists (`IptvPlaylistAccess`).

**Passthrough tokens** are encrypted with a time-limited DataProtection protector (`IIptvPassthroughTokenProtector`, purpose `Vora.Iptv.PassthroughToken.v1`) and carry the tag, playlist id, channel id and lease, so a segment or audio fetch applies that provider's headers and a client can't read the upstream address. The playlist, segment and audio endpoints stay anonymous and token-only. Tokens from before this change no longer open.

**Limits.** The tuner limit is `MaxConcurrentStreams`, or the account's `max_connections` when the admin left it at 0 (`IptvConnectionLimit`); paid providers ban accounts that go over. Timeshift, DVR, HLS passthrough, the live remux below and, on a playlist with a limit, passthrough audio each hold a lease; the audio lease is renewed while the stream pipes and released when it ends. A viewer (the profile, or the account for an account-level token) holds one lease per playlist: a new passthrough start or a timeshift start on that playlist releases the viewer's earlier passthrough leases, so changing channel on a one-connection account doesn't wait 90 seconds for the old lease to lapse. Health checks default off for Xtream accounts: each probe is a connection.

**Guide.** An active Xtream account with `UseProviderGuide` (default on) has its `xmltv.php` read with its login during every guide sync, after the configured EPG sources, so it fills only the channels they don't cover. The credentialed address is built for the request and stored nowhere; Match Diagnostics lists it as "{name} (provider guide)" with the host only.

**Logs.** `IptvUrlRedactor` masks userinfo, credential-like query values and Xtream `/live|movie|series|timeshift/{user}/{pass}/` and `/hlsr/{token}/{user}/{pass}/` segments, plus the playlist's own password wherever it appears. Every IPTV log line and `LastError` that can hold an address goes through it, the DVR ffmpeg output included. The IPTV `HttpClient` logs through `IptvHttpClientLogger` (address without query, redacted, Debug) instead of the framework logger, which printed Xtream paths, and timeshift ffmpeg output is read by the server and logged redacted instead of going to the container console.

**Admin.** `POST /api/iptv/admin/playlists/test-connection` (`TestIptvPlaylistConnection`) checks a source without saving it and returns `IptvConnectionTestVM` (account status, expiry, max connections, channel count, or a readable error); with `playlistId` it uses that playlist's saved password. On create and update every new field is optional; on update a field left out is unchanged and an empty string clears it. Changing the type, address, login or headers reloads the channels. The web form (`components/Admin/Iptv/IptvSourceFields.tsx`) is shared by the Live TV and Internet Radio admin pages, the edit dialog and the setup guide.

## Passthrough of raw MPEG-TS video (live remux)

`CanTimeshiftIptv` is off for ordinary profiles, so they watch Live TV through passthrough, and every Live TV player hands the passthrough URL to its HLS pipeline. Passthrough only proxies HLS; a non-HLS stream (a `.ts` address, which most paid M3U providers serve, an Xtream account without `m3u8` in `allowed_output_formats`, or an address whose response isn't an HLS playlist) used to come back as `streamType: "audio"` and could not be watched by them on any client.

For a **TV-kind** channel whose stream isn't HLS, `POST /api/iptv/passthrough/start` now starts a live remux (`IptvManager.StartLiveRemuxAsync`, decided by `IptvPassthroughService.NeedsRemux`) and returns its HLS URL with `streamType: "hls"`; the request and response shapes are unchanged and no client needs to change. Radio-kind channels keep the audio passthrough.

- **It is the timeshift plumbing with a short window.** The same ffmpeg builder (`BuildTimeshiftProcess` with `rollingWindow`), the same provider headers and login (`IptvRequestPolicy`), the same session folder under `TranscoderTempDirectory/timeshift/{viewer}/{session}`, the same signed `/api/streaming/hls/timeshift/...` URL and the same `timeshift:{viewer}` tuner lease. The video is copied (`-c:v copy`, never re-encoded and no `LiveVideoDecision`); audio goes to stereo AAC like timeshift, so a channel with AC-3 or MP2 audio still plays in a browser. The playlist keeps `LiveRemuxWindowSegments` (6) four-second segments and ffmpeg deletes older ones (`delete_segments`), so a session is a few segments on disk. It does not need `CanTimeshiftIptv`: there is no rewind beyond those segments.
- **One live session per viewer.** `TimeshiftCoordinator` registers it as `LiveSessionKind.Remux` under the viewer's key. A second start on the same profile, a channel change or a timeshift start replaces it (the old ffmpeg is stopped and its folder deleted before the new one starts); a TV passthrough start for an HLS channel ends it (`StopLiveRemuxAsync`, which leaves a timeshift alone); `/api/iptv/timeshift/stop` ends it too.
- **Idle cleanup.** Every fetch of the session's playlist or segments counts as a heartbeat (`DvrPlaybackEndpoints.ServeTimeshiftHlsAsync`). `TimeshiftJanitorWorker` stops a remux nobody has fetched from for `TimeshiftCoordinator.LiveRemuxMaxIdle` (60 s; timeshift keeps its 2 minutes, since its players ping), deletes its folder and releases its lease; orphaned folders are reaped like timeshift's.
- `LiveRemuxTests` (real ffmpeg, skipped without it) checks the headers reach the provider, the window stays at six segments, a restart replaces the session and stop and idle eviction clean up.

## DVR session statuses

The DVR Dashboard groups sessions into three tabs (`Completed`, `Upcoming`, `Failed`) by mapping statuses (`utils/dvrDisplay.ts`, which also holds the labels and token colours):

- Upcoming: `Pending`, `Recording`
- Completed: `Completed`, `PostProcessing`, `DetectingCommercials`
- Failed: `Failed`, `Conflict`, `Cancelled`

`PostProcessing` and `DetectingCommercials` recordings have finished but aren't ready yet; playback is disabled until they reach `Completed`. The dashboard used to look for `Post-Processing` and `Completed (Partial)`, names the server never sends, so processing recordings fell into the Failed tab.

**Playback is owner-only.** `POST /api/streaming/dvr/play/{sessionId}` signs a file URL only for an admin or the account that owns the recording's schedule (`DvrEndpoints.CallerOwnsSession`, the same rule as delete and cancel-series); anyone else gets a 404.

## Series recording vs single

When the user records a series, the schedule has `isSeries = true` and an optional `keepMaxEpisodes` value (0 = keep all). The backend creates sessions for matching programs as the EPG predicts them, and cancels the oldest when retention is hit.

Cancelling a session that belongs to a series prompts the user: cancel this one episode (`deleteDvrSession`) or stop the whole series (`cancelDvrSeries`).

## Timeshift

Timeshift lets the user pause/rewind live TV. `POST /api/iptv/timeshift/start` returns a fresh HLS URL backed by a server-side buffer. `ping` keeps the buffer alive while watching, `stop` releases it.

**What the player can take.** `start` may carry the player's video limits: `videoCodecs` (ffprobe codec names), `maxH264Level` (level × 10, so 42 is 4.2) and `h264Profiles` (`baseline`, `main`, `high`, …). The Roku sends all three; the web and Android send none, and with none the video is copied (`-c:v copy`) exactly as before. Each channel keeps the video format its last copying session saw (`IptvChannel.VideoCodec`, `VideoProfile`, `VideoLevel`, `VideoHeight`, `VideoFrameRate`, `VideoInterlaced`, `VideoFormatSeenAt`), read by ffprobe from that session's own first segment (`seg_000.ts`, `ITimeshiftVideo.ReadFormatAsync`), so learning it never opens a second connection to the provider. A playlist refresh that changes the channel's stream address clears it. `LiveVideoDecision` re-encodes when the codec isn't listed, or an H.264 stream's profile isn't listed (`Constrained Baseline` counts as `baseline`) or its level is above the maximum, and copies otherwise. A known format the player can't take encodes from the start. An unknown or changed one starts copying; if its first segment shows the player can't take it, the session restarts encoding before the address goes back, so the player never sees it. The encode (`TimeshiftVideoArgumentsBuilder`) is 8-bit H.264 High at 4.1, or 4.2 above 30 fps, never above the player's maximum, with a keyframe on every 4 s segment, deinterlaced (`yadif`) when the source is interlaced and scaled to 1080 lines when taller. It uses the GPU the DVR would (`HardwareEncoderChoice`), restarts on the CPU if a GPU run dies before its playlist appears, and stays a copy when video transcoding is turned off. A session that encodes doesn't relearn the format; the next copying session does.

**Permission gating in the client.** The Live TV skip-back/forward and record buttons are gated by `canTimeshiftIptv` / `canRecordLiveTv`; web radio uses the same timeshift decision (`components/Player/useLivePermissions.ts`, admins included). These are user-level (account-level) settings, edited in the admin `UserAccessModal`. Permissions are written into the profile JWT at sign-in, but they go stale after admin edits — so `useLivePermissions` calls `userService.getUserAccount(userId)` on mount and overwrites the JWT-derived values with the fresh server response. New player surfaces that gate on these claims should follow the same pattern.

## Per-profile favorites, per-device guide prefs

**Favorites belong to the profile.** Favorite Live TV channels and favorite radio stations live in `ProfileChannelFavorites`, keyed by `(ProfileId, PlaylistId, ExternalChannelId)` — not by channel row id, so a channel that drops out of an M3U and comes back (a new row) is still a favorite, and a restore onto a server whose channels were re-created still lines up. TV vs radio follows the channel's `Kind`. The table is backed up by the `users.channel-favorites` section.

The clients keep using the prefs endpoints they already had; `IChannelFavoritesManager` sits behind them:

- `GET .../devices/{deviceId}/iptv` writes the profile's TV favorites into `favoriteChannels` (external channel ids). `PUT` replaces the profile's TV favorites with the ids it sends, then stores the rest of the JSON per device without them.
- `GET/PUT /api/users/profiles/{profileId}/radio-prefs` (and the old per-device `/radio`) do the same with `favoriteIds` (channel ids).
- **A PUT without the favorites field leaves favorites alone**, so a screen that only edits providers or hidden channels can't wipe them. The web Providers settings tab relies on this.
- A change fires `ChannelFavoritesUpdated` (profile group) so the guide and the Home favorites rows on other devices refresh.

The `ChangesSinceInitial` migration copies every device's `favoriteChannels` (union per profile, matched case-insensitively to TV channels) and every profile/device `favoriteIds` into the table; unparseable JSON is skipped rather than failing the migration.

The rest of the guide state stays **per device** in `ProfileDeviceSetting.IptvPrefsJson`: enabled providers, hidden channels, region and resolution filters, "Hide empty channels". That is what disappears if the `ClientDevice` row gets orphaned because the `X-Vora-Device-Id` header isn't matched correctly — see `docs/auth-and-devices.md`. The web guide reads the server copy first and only falls back to its `iptv_prefs_*` localStorage cache when the server can't be reached.

## Frontend pages

- `pages/Client/LiveTv/LiveTvPage.tsx` — main entry into Live TV.
- `pages/Client/LiveTv/LiveTvGuide.tsx` — the program grid (channels × time).
- `pages/Client/LiveTv/DvrDashboard.tsx` — recordings dashboard with the three tabs.
- `pages/Client/Audio/AudioHubPage.tsx` — Radio tab uses the same M3U + channels infrastructure, filtered to `Kind === 'Radio'`.
- `pages/Admin/Iptv/IptvPage.tsx` — shared admin page that takes a `kind: IptvChannelKind` prop. Mounted twice: `/admin/live-tv` (TV playlists + EPG sources + Match Diagnostics) and `/admin/internet-radio` (Radio playlists only). The "Radio Playlist" checkbox is gone — the active route determines kind.

`components/Iptv/GuideProgramModal.tsx` is the per-program record/play modal triggered from the guide. `components/Dvr/DvrSessionCard.tsx` is the recording card used in the dashboard. `components/Player/Panels/LiveTvRecordModal.tsx` is the same record dialog reached from the player.

## Player

Live TV uses **`components/Player/LiveTvPlayer.tsx`** instead of `GlobalVideoPlayer`. `MainLayout` picks which player to mount based on `currentMedia.playbackContextType === 'LiveTv'`. The two players share `Controls/` and `Panels/` primitives but have different chrome (channel up/down, CC toggle, embedded guide, etc.).
