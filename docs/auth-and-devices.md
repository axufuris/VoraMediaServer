# Auth, profiles, and devices

How sign-in, profiles, and per-device tracking fit together end to end.

## Two-token model

Sign-in is two steps and gives two JWTs, plus a refresh token for the clients that use one. Lifetimes, claims, stamps, refresh, lockout and rate limits are in [`docs/auth-tokens.md`](auth-tokens.md).

- **Account token** — proves who owns the account. Returned as `accessToken` from `/auth/login`, `/auth/register`, `/auth/setup` and `/auth/refresh`. The user picks a profile after it.
- **Profile token** — bound to one profile under the account. Used for nearly every request. Obtained from `/auth/exchange-profile-token` once the user picks a profile (and enters its PIN, if it has one).

The profile token carries the claims `Vora.Api/Extensions/AuthExtensions.cs` reads: `sub` (the profile id, `GetProfileId`), `accountId` (`GetAccountId`), `isAdmin` (the account's server-admin flag, which the `AdminOnly` policy requires to be `True`), and the library, rating and permission claims.

**Always** use the `AuthExtensions` helpers. Never call `user.FindFirst("...")` directly.

## Auth pages

- `pages/Auth/LoginPage.tsx` — Email + password only. Derives the server URL from `import.meta.env.VITE_API_BASE_URL` or falls back to `window.location.origin` and probes it on mount. **No** server URL input shown to the user. Renders a "Forgot password?" link only when `setup-status.emailEnabled` is true.
- `pages/Auth/RegisterPage.tsx` — Probe the current-origin server, then show the register form. Reads `?invite=<token>` from the URL; if present, calls `/auth/invitations/validate` to pre-fill and lock the email field, hides the PIN field, and registers whatever the `registrationMode`. Otherwise by mode: `0` Disabled shows "Registration Disabled", `1` Open shows the form, `2` Invite PIN adds a 4-digit PIN field (digits only), `3` Invitation only shows "Invitation Required".
- `pages/Auth/ForgotPasswordPage.tsx` — Vague success state regardless of whether the email exists. Shows a "contact your administrator" fallback when the server reports email is disabled.
- `pages/Auth/ResetPasswordPage.tsx` — Reads `?token=<token>` from the URL, accepts new password + confirm, POSTs to `/auth/reset-password`.
- `pages/Auth/ConfirmEmailPage.tsx` — Reads `?token=<token>` from the confirmation link and POSTs it to `/auth/confirm-email-change`.
- `pages/Auth/SetupPage.tsx` — First-run server-claim flow.
- `pages/Profile/ProfileSelectionPage.tsx` — Picks a profile after account-token login and exchanges for a `profile_token`, sending the PIN with the exchange (it does not call `validate-pin`; a 403 there just re-prompts).

When the user adds **additional** servers, that flow lives in `components/Layout/ServerManagerModal.tsx` — which **does** show a server URL field (because the additional server is by definition not the current origin).

## Auth endpoints

All under `/api/auth/*`, anonymous unless noted. See `Vora.Api/Endpoints/AuthEndpoints.cs`. The rate limits are in `docs/auth-tokens.md`.

| Route | Notes |
| --- | --- |
| `GET /setup-status` | Returns `{ isClaimed, registrationMode, serverName, emailEnabled }`; `registrationMode` is the number below. The frontend uses `emailEnabled` to decide whether to render Forgot Password / Email Invitations UI. |
| `POST /setup` | First-run claim: creates the admin account and its first profile; body also takes `serverName`. 400 once the server is claimed. |
| `POST /login` | Email + password, returns `accessToken` and `refreshToken`. 403 "Password sign-in is turned off" for a non-admin when an admin has turned password sign-in off (`docs/sso.md`); an account without a password never signs in here. A failure is a bare 401; the server log says why (warning: no account with that email, wrong password with the account's id and the password's length, or locked out). It never holds the email that was typed or the password: no log message carries an email address (CodeQL `cs/exposure-of-sensitive-information`). |
| `POST /register` | Body: `{ email, password, displayName, secretCode?, inviteToken? }`. If `inviteToken` is present, validates it via `InvitationManager`, forces the email to match the ticket, and bypasses every `RegistrationMode`. Otherwise enforces `RegistrationMode` (Disabled and Invitation are refused; SecretWord needs `secretCode`). 400 with a message on any refusal. |
| `POST /refresh`, `POST /logout` | Trade or revoke a refresh token. The web client never calls them. See `docs/auth-tokens.md`. |
| `POST /exchange-profile-token` | **Needs a token** (any valid one of the account). Query `accountId` must be the caller's, plus `profileId`; optional body `{ pin }`. Returns `{ token }`, the profile token. |
| `POST /forgot-password` | Body: `{ email }`. Always returns 204 (no enumeration leak). Throttled 3/hour/email via `IMemoryCache`. Silently no-ops if email is disabled. |
| `POST /reset-password` | Body: `{ token, newPassword }`. 204 on success; 400 "Password must be at least 8 characters." or a generic "Invalid or expired" message. Rotates the account's security stamp. |
| `POST /confirm-email-change` | Body: `{ token }`. Applies an email change asked for in account settings: 204, 409 if the address was taken meanwhile, 400 for a bad or expired (60 minutes) link. |
| `POST /invitations/validate` | Body: `{ token }`. Returns `{ email, expiresAt }` for a valid invite, 404 otherwise. |
| `GET /providers` | The sign-in providers that are turned on, `passwordSignInEnabled` and `registrationMode` (a string), for the login page. `docs/sso.md`. |
| `GET /oidc/{slug}/start`, `GET /oidc/{slug}/callback` | The OpenID Connect redirect flow; both answer 302 to the web app. `POST /oidc/{slug}/link` (needs a token) starts a link from Account settings. `GET /external/pending/{handle}`, `POST /external/complete` finish a sign-in that needs a step. `docs/sso.md`. |
| `POST /external/exchange` | Body: `{ code }`. Trades a one-time sign-in code for the tokens of a login (`AuthStrict`); 401 for a used, expired or unknown code. |
| `GET /identities`, `DELETE /identities/{id}` | **Needs a token.** The caller's linked sign-in providers; unlinking the last way in is 409. |

Admin-only endpoints (`AdminOnly` policy):

| Route | Notes |
| --- | --- |
| `POST /invite-code` | Creates a `RegistrationTicket`: a random **4-digit PIN**, valid 30 minutes, shared (anyone with it can register) and deleted on first use. |
| `GET /invitations` | Lists outstanding `InvitationTicket` rows (per-email, single-use, hashed token). |
| `POST /invitations` | Creates a per-email invite (7 days by default, up to 60), sends `AdminInvite` email. Returns 400 if email is disabled, 409 if the email already maps to an account. |
| `DELETE /invitations/{id}` | Revokes an invite. |

`POST /api/users` (AdminOnly) adds an account without an invite; see below.

## Registration modes vs invitations

`RegistrationMode` (`GET /setup-status`; set on **Users & Access** or by `PUT /settings/registration-mode`) is a number: `0` Disabled, `1` Simple (the admin page calls it Open: anyone can sign up), `2` SecretWord (the admin page calls it Invite PIN), `3` Invitation (invitation only, from an emailed link). The mode only governs people signing **themselves** up.

There are two parallel invitation systems and they coexist:

- **`RegistrationTicket` (Invite PIN)**. Used when `RegistrationMode == SecretWord`. A 4-digit PIN shared by whoever gets it, valid 30 minutes, deleted when one person registers with it. `AuthManager.GenerateInviteCodeAsync` creates it; **Generate PIN** on the **Users & Access** page shows it.
- **Per-email `InvitationTicket`** (email invitations). Independent of `RegistrationMode` — works regardless of the mode setting, including `Disabled`. Each invite is tied to one email address, requires the recipient's email to match at registration, and is consumed on use. Surfaced on the **Email Invitations** admin page and as the mode-3 action on Users & Access. Needs email enabled. See `docs/email.md`.
- **Admin-created accounts**: `POST /api/users` (AdminOnly, `AuthManager.CreateUserAsAdminAsync`) adds an account with a starting password (8 characters or more), regardless of `RegistrationMode`. With `ssoOnly: true` it adds the account without a password (`CreatePasswordlessUserAsAdminAsync`), which links on the person's first verified provider sign-in (`docs/sso.md`). Same defaults and first profile as a registration; 409 for an email in use, 400 with a `detail` for bad input. Surfaced as **Add user** on **Users & Access**, which also shows the mode selector and the action that fits it (open → sign-up link, PIN → generate, invitation → send email).

Every new account gets a first profile named after it, flagged `UserProfile.IsAdmin` (the web keeps it as `is_profile_admin` and offers account management only to that profile; the server does not check it). Only the first-run claim makes an account a server admin; nothing promotes one afterwards.

`AuthManager.RegisterAsync` checks `inviteToken` first; if present, it takes the invitation path. Otherwise it falls through to the `RegistrationMode` flow.

## localStorage keys
These are the canonical keys. Don't invent new ones in a vacuum.

| Key | Purpose | Set by |
| --- | --- | --- |
| `device_id` | Per-browser stable UUID. Resolved by `getOrCreateDeviceId()` (`utils/deviceId.ts`) on first load, **mirrored into a long-lived `vora_device_id` cookie** so clearing localStorage or storage eviction restores the same id instead of minting a new device record. **Survives logout.** | `App.tsx` / `utils/deviceId.ts` |
| `account_token` | Account JWT after login | `LoginPage`, `RegisterPage` |
| `profile_token` | Profile JWT after profile selection | `ProfileSelectionPage` |
| `user_id` | Account user ID | `LoginPage`, `RegisterPage` |
| `profile_name` | Display name of active profile | `ProfileSelectionPage` |
| `profile_name_<serverId>` | Per-server profile name (multi-server) | `ServerManagerModal` |
| `is_server_admin` | `"true"` if the underlying user has `isAdmin` on the server. **THE** admin flag. | `ProfileSelectionPage`, `ServerManagerModal` |
| `is_profile_admin` | `"true"` if the active profile is the admin profile | `ProfileSelectionPage` |
| `auto_login_profile_id` | Skips profile picker on next load | `ProfileSelectionPage` |
| `vora_admin_theme_id` | Active admin theme id. First-paint cache so the admin doesn't flash the default theme on every load; backend `/api/admin/themes/active` is the source of truth and reconciles after mount. | `theme/ThemeProvider.tsx` |
| `vora_client_template_id` | Active client template id. First-paint cache for `ClientTemplateProvider`; backend reconciles after mount. | `theme/ClientTemplateProvider.tsx` |
| `playback_prefs_<profileId>_<deviceId>` | Per-profile/device bandwidth + max resolution + audio output JSON, read for the profile of the server the media plays from (`getCurrentProfileDeviceIds(serverId)`). `maxAudioChannels` is the viewer's Audio output pick: 0 for Automatic, or 2 / 6 / 8. It is sent to the server as `pickedAudioChannels`, beside what the browser detected (see `docs/track-selection.md`). `seekStepSeconds` (5 / 10 / 15 / 30) and `volumeStepPercent` (2 / 5 / 10) are the video player's arrow-key steps; missing or any other value reads as 10 and 5 (`readPlayerKeySteps`, `docs/frontend-client-ui.md`). Mirrored server-side via `profileDeviceSettingsService`; `GET /api/users/profiles/{profileId}/devices/{deviceId}/playback` (`ClientPlaybackSettingsDto`) returns both steps, 10 and 5 for a blob saved before them, so the TV apps read what was saved there. | `SettingsPage` (Playback tab) |
| `iptv_prefs_<profileId>_<deviceId>` | Offline cache of the device's guide prefs (providers, hidden channels, filters). The server copy wins on load; favorites in it are the profile's, merged in by the server. | `SettingsPage` (Providers tab), `useGuideData` |
| `music_sub_tab` | Music page sub-tab — `forYou`, `artists`, `albums`, or `playlists`. Device-local display preference; an unrecognised value falls back to `forYou`. | `MusicTab` (`pages/Client/Audio/Music/musicSubTab.ts`) |
| `now_playing_lyrics`, `now_playing_synth`, `now_playing_synth_style`, `now_playing_synth_colors` | Music Now Playing view: lyrics and the Synth visualizer (`"true"` / `"false"`, absent means off), the Synth's style (`bars` / `waves` / `ring`, default `bars`) and colours (`art` / `theme`, default `art`). Device-local; an unknown value reads as the default. | `NowPlayingFullscreen` (`utils/nowPlayingView.ts`) |
| `vora_library_sort_<profileId>_<libraryId>` | JSON `{ sortBy, sortDir, chosenUnder }`: a sort picked on a library's page, ignored once the profile's library sort is no longer `chosenUnder`. Replaces `vora_library_sort_<libraryId>` / `vora_library_sortdir_<libraryId>`, which are read while the profile is on the server default and removed on the next pick (`docs/frontend-client-ui.md`). | `LibraryPage` (`utils/librarySort.ts`) |
| `audio_quality_preference`, `audio_crossfade_seconds`, `audio_eq_preset` | Music audio settings: stream quality, crossfade seconds (0–12), EQ preset. Device-local. | `NowPlayingFullscreen` (`utils/audioQuality.ts`) |
| `calendar_view_mode_v2` | Release Calendar layout — `month`, `week`, or `day`. Device-local, not mirrored: it is a display preference, not a profile setting. Nothing saved, or an unrecognised value, opens on `week`. Renamed from `calendar_view_mode` when the default changed from Month, so a Month saved under the old default doesn't pin existing browsers there; the old key is simply ignored. | `CalendarPage` |
| `sidebar_collapsed` | `"true"` while the client sidebar is folded to its icons (tablet width and up); anything else, or nothing, shows it in full. Device-local. | `MainLayout` (`layouts/parts/useSidebarCollapsed.ts`) |
| `desktop_mpv_notice_dismissed` | `"true"` once a Vora Desktop user on Linux dismissed the notice that mpv is missing and videos play in the built-in player. Device-local. | `MainLayout` (`components/Desktop/MpvMissingBanner.tsx`) |
| `Vora_servers_vault`, `Vora_active_server_id` | The connected servers (a JSON array, see Multi-server clients) and the active one's id. | `serverVault` (`utils/serverVault.ts`) |

**`is_admin` is dead.** It is never written by any current code. If you find code reading `is_admin`, replace it with `is_server_admin`.

`sessionStorage` is used for **pending pre-vault** state during the login/setup flow (`pending_server_url`, `pending_user_token`, `pending_user_id`, `pending_server_name`), for `fresh_server_setup` (set by the first-run claim so the first admin lands on the Setup Guide), and for `setup_guide_prompted_<serverId|local>`, which stops the Setup Guide pop-up asking twice in one browser session (see `docs/setup-guide.md`), and for `new_build_reload_at`, the time a tab last reloaded to pick up a new build, so a page that keeps failing to load reloads once rather than forever (see `docs/frontend-conventions.md`).

## Times on the wire

The server stores and emits **UTC**; every client renders in the viewer's own
zone. Two pieces make that hold:

- `UtcDateTimeConverter` / `NullableUtcDateTimeConverter` (registered in
  `AddVoraJsonOptions`) write every `DateTime` as `…Z`, converting a `Local`
  value and stamping an `Unspecified` one. Without it, System.Text.Json emits an
  `Unspecified` instant with no zone at all and `new Date(...)` in the browser
  reads it as local time — the same string meaning a different moment per client.
  Inbound, the converters accept `Z`, an offset, or a zone-less string and hand
  the app a `Kind=Utc` value.
- `Vora.Web/src/utils/serverTime.ts` is the client half: `parseServerDate`,
  `serverTimeMs`, `formatTime`/`formatDate`/`formatDateTime`, `yearOf`,
  `isSameLocalDay` and `calendarDaysBetween`. Nothing renders a server timestamp
  with a bare `new Date(...)`.

**Date-only values are not instants**, all the way down. A release date, an air
date or a birthday is a calendar date and must read the same everywhere, so it is
`DateOnly` in the domain, a `date` column in PostgreSQL, and `"2026-01-01"` on the
wire — `DateOnlyConverter` / `NullableDateOnlyConverter`. The converters read
leniently (a client still sending `2026-01-01T00:00:00Z` keeps working, and its
date is taken as written rather than shifted) and always write the plain form.
Client-side, `serverTime` builds local midnight for that shape and `yearOf` reads
the year off the string.

The fields: `MediaItem.ReleaseDate` (inherited by movies, shows, seasons,
episodes and tracks), `Movie.TheatricalReleaseDate` / `DigitalReleaseDate`,
`TvShow.LastAirDate` / `NextAirDate`, `Actor.Birthday` / `Deathday`, and
`ExpectedReleaseDate` on requests and watchlist items. `CalendarEventDto.ReleaseDate`
stays a `DateTime` on purpose — it carries a TV airing's time of day alongside
`AirTime`, so it is a real instant.

## Live permission refetch

The profile JWT carries permission claims (`canTimeshiftIptv`, `canRecordLiveTv`, etc.) as they were when it was issued. An admin edit of the account's access or of the profile rotates the security stamp, so the old token is refused within about 30 seconds (`docs/auth-tokens.md`), and the hub sends `UserAccessUpdated` / `ProfileAccessUpdated`, which `MainLayout` answers with an alert, dropping `profile_token` and returning to the profile picker. Until then the claims can be out of date.

Players that gate UI on those permissions (e.g. `LiveTvPlayer`'s skip-back/forward and record buttons) read the JWT (the active server's own, `tokenClaimsForServer`) for the initial render, then immediately call `userService.getUserAccount(userId)` (that server's account, `userIdForServer`) to refresh from the server and overwrite the stale value. New player surfaces that gate on user-level permissions should follow the same pattern — JWT for the first paint, server fetch for the truth.

## Sign-out

`authService.logout()` clears `account_token`, `profile_token`, `user_id`, `profile_name`, `is_server_admin`, `is_profile_admin` and the entire `sessionStorage`, drops the cached API clients and the SignalR connection, and goes to `/login`. It does **not** clear `device_id` — that key persists across logouts so the user's favorites/preferences stay attached to the same device record. It does not call `/auth/logout` either: the web client holds no refresh token.

`MainLayout.handleSignOut` (and the admin shell's account menu) ask for confirmation, call `serverVault.clearVault()` to drop the multi-server vault, then run `logout()`.

A 401 on any request (`handleUnauthorized` in `api/client.ts`) clears `profile_token`, `account_token` and the two admin flags and goes to `/login`; it leaves the vault and `user_id` alone.

## Device tracking

Every authenticated request goes through `Vora.Api/Middleware/DeviceTrackingMiddleware.cs`. It:

1. Reads the **`X-Vora-Device-Id`** header (mandatory — no header = no tracking, request continues anyway).
2. A device found blocked in the last 5 minutes gets 403 ("This device has been blocked by the server administrator.") on any request. An unauthenticated request, or one with no account id, otherwise continues untracked.
3. Otherwise, at most once per 5 minutes per device, looks up the `ClientDevice` row by `DeviceId`. Blocked: 403. New: inserts a row from the companion headers (all sent by `api/client.ts`), each falling back to `Unknown Client` / `Unknown Device` / `Unknown Type` / `Unknown OS`:
   - `X-Vora-Client` → `ClientName` (≤64)
   - `X-Vora-Device` → `DeviceName` (≤128)
   - `X-Vora-Device-Type` → `DeviceType` (≤32)
   - `X-Vora-OS` → `OperatingSystem` (≤64). The frontend sends a parsed short name (`Windows 10/11`, `macOS`, `iOS`, …) from `DETECTED_OS` in `client.ts` — **never** the raw `navigator.userAgent` (overflows the column).
   - Inside Vora Desktop (`window.voraDesktop`) the four are `Vora Desktop` (`Vora Desktop QA` on a QA build), that name with the platform (`Vora Desktop (Windows)`, `Vora Desktop QA (Linux)`, …), `Desktop`, and `Windows` or `Linux`. The type is free text; the admin Devices page prints it as sent.
4. For an existing row, updates `LastConnectedAt`, `LastIpAddress`, `LastUserId`, `LastProfileId` and any companion header the request carried, so a renamed client shows its new name. Geo-looks-up the IP (`ClientAddress`, ipwho.is over HTTPS, no key) via the named HttpClient `DeviceTrackingMiddleware.GeoLookupHttpClientName` when the IP changes or the device still has no location. Private and reserved addresses (incl. Docker's 172.16.0.0/12 and Tailscale's 100.64.0.0/10) are "Local Network" and never sent out. `X-Forwarded-For` is honoured **by default** from private-network peers (`ClientAddress.PrivateNetworks`, `ForwardLimit` 5, read right to left, so it can't be spoofed from the internet); `ForwardedHeaders:KnownProxies/KnownNetworks` narrow it and `Enabled=false` turns it off. 100.64/10 (Tailscale) is labelled VPN and counts as remote for streaming limits. `StreamManager` uses the same `ClientAddress.IsLocal`.
5. Caches the device for 5 minutes to avoid re-upserting on every request.

`DeviceEndpoints.UpdateCapabilitiesAsync` (`PUT /api/devices/capabilities`) reads the same `X-Vora-Device-Id` header to attach codec/container/audio-channel capabilities to the device row, and names a row it has to create from the same companion headers (`ReadDeviceIdentity` in `Extensions/DeviceHeaderExtensions.cs`, which the middleware uses too; the `DeviceIdentity.Unknown…` names when one is missing). `StreamingEndpoints` start-session does the same. **Header naming is universal: `X-Vora-Device-Id`.** Older code may reference `X-Device-Id`; treat any sighting as a bug and fix it.

### Device identity durability & native clients

`DeviceId` is an **opaque string** — the middleware upserts on whatever the client sends, so identity is entirely the client's choice of value; the server needs no change to accept a different kind of id.

- **Web** can't read any hardware identifier (browsers block MAC/serial for privacy), so it uses a generated UUID. That id is only as durable as browser storage, hence the `vora_device_id` cookie backup above. A full site-data clear or a different browser is still a new device row — an inherent web limitation. The UUID comes from `crypto.randomUUID` where the page has it and from `crypto.getRandomValues` otherwise: a server opened over plain `http://` by IP or host name is not a secure context and has no `randomUUID`, and the unguarded call once left a blank page on the first visit. `utils/copyText.ts` covers the same gap for `navigator.clipboard`.
- **Native clients** send a device id that lives with the install, plus the same companion `X-Vora-*` headers (Client / Device / Device-Type / OS), clamped to the column sizes. No MAC anywhere — Apple and Google both hide/randomize it.

  | Client | Device id | `X-Vora-Client` | Device / type / OS |
  | --- | --- | --- | --- |
  | Android, Fire TV | UUID made on first run, kept in app preferences; survives updates and cache clears, not uninstall | `Vora Android` | `MANUFACTURER MODEL`; `Android`, `Android TV` or `Fire TV`; `Android <release>` |
  | Roku | `roDeviceInfo.GetChannelClientId()`, stable per channel per device, survives reinstalls | `Vora Roku` | the model name; `Roku`; `Roku OS <major>.<minor>` |
  | Apple TV | lower-case UUID made on first launch, kept in UserDefaults; a deleted app gets a new one | `Vora tvOS` | `Apple TV (<machine>)`; `Apple TV`; `tvOS <x.y.z>` |

## Per-profile data preservation across purge

Per-profile ratings + watch-state don't die with a purged media item. When an item is permanently removed (Media Trash auto-purge or manual `DELETE /media/trash/{id}` — see `docs/scanning-and-tasks.md`), each profile's rating and watch-state is first archived into the `PreservedUserMediaData` table, keyed by a **content identity** rather than the item's `MediaItem.Id`:

- `ContentIdentity.Compute` (`Vora.Application/Media/ContentIdentity.cs`) builds a stable key from external ids — movies/shows as `type:tmdb|imdb|tvdb:<id>`, episodes as `episode:<series id>:<season>:<episode>`.
- When the same content is later re-added (a different `MediaItem.Id`, same content key), the archived rating + watch-state is restored to each profile.

This is why removal is non-destructive to user data even though the row's GUID changes on re-add. The table schema is in `docs/database.md`.

## Multi-server clients

`utils/serverVault.ts` stores an array of connected `VoraServer` records (`id`, `name`, `url`, `token`, `profileId`, `isAdmin`) under `Vora_servers_vault`; `token` is that server's **profile** token (the account token is not kept) and the id is `server_<timestamp>` or `legacy_server`. The active server is tracked separately (`Vora_active_server_id`), and a profile's name for a server sits in `profile_name_<serverId>`. `apiClient` calls accept `{ serverId }` to target a non-active server. `ServerManagerModal` is the UI for adding/switching/removing servers.

Profile tokens are scoped to a server, and so is the identity a request names: take the profile id, account id and permission claims from `profileIdForServer` / `userIdForServer` / `tokenClaimsForServer(serverId)` (`api/client.ts`, see `docs/frontend-conventions.md`), never from `profile_token` or `user_id`, which belong to the primary server. When the user signs out of one server but stays on others, only that server's vault entry is removed.
