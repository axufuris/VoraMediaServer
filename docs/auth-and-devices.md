# Auth, profiles, and devices

How sign-in, profiles, and per-device tracking fit together end to end.

## Two-token model

The backend issues two distinct JWTs:

- **`account_token`** — proves who owns the account. Returned from `/auth/login` and `/auth/register`. The user picks a profile after login.
- **`profile_token`** — narrower token bound to a specific profile under the account. Used for nearly every authenticated request. Obtained by calling `/auth/exchange-profile-token` after the user selects a profile.

The `profile_token` payload includes claims read by `Vora.Api/Extensions/AuthExtensions.cs`:

- `sub` — profile ID (via `GetProfileId`)
- account ID (via `GetAccountId`)
- `IsAdmin` — server-admin boolean (the basis of the `AdminOnly` policy)
- library / content-rating allowlists

**Always** use the `AuthExtensions` helpers. Never call `user.FindFirst("...")` directly.

## Auth pages

- `pages/Auth/LoginPage.tsx` — Email + password only. Derives the server URL from `import.meta.env.VITE_API_BASE_URL` or falls back to `window.location.origin` and probes it on mount. **No** server URL input shown to the user. Renders a "Forgot password?" link only when `setup-status.emailEnabled` is true.
- `pages/Auth/RegisterPage.tsx` — Probe the current-origin server, then show the register form. Reads `?invite=<token>` from the URL; if present, calls `/auth/invitations/validate` to pre-fill and lock the email field, hides the secret-code field, and registers even when `registrationMode === 0` (Disabled). Otherwise behaves per `registrationMode` (open / disabled / needs 3-word secret code).
- `pages/Auth/ForgotPasswordPage.tsx` — Vague success state regardless of whether the email exists. Shows a "contact your administrator" fallback when the server reports email is disabled.
- `pages/Auth/ResetPasswordPage.tsx` — Reads `?token=<token>` from the URL, accepts new password + confirm, POSTs to `/auth/reset-password`.
- `pages/Auth/SetupPage.tsx` — First-run server-claim flow.
- `pages/Profile/ProfileSelectionPage.tsx` — Picks a profile after account-token login, exchanges for a `profile_token`.

When the user adds **additional** servers, that flow lives in `components/Layout/ServerManagerModal.tsx` — which **does** show a server URL field (because the additional server is by definition not the current origin).

## Public auth endpoints

All under `/api/auth/*`, anonymous unless noted. See `Vora.Api/Endpoints/AuthEndpoints.cs`.

| Route | Notes |
| --- | --- |
| `GET /setup-status` | Returns `{ isClaimed, registrationMode, serverName, emailEnabled }`. The frontend uses `emailEnabled` to decide whether to render Forgot Password / Email Invitations UI. |
| `POST /setup` | First-run server claim. |
| `POST /login` | Email + password, returns `account_token`. |
| `POST /register` | Body: `{ email, password, displayName, secretCode?, inviteToken? }`. If `inviteToken` is present, validates it via `InvitationManager`, forces the email to match the ticket, and bypasses `RegistrationMode.Disabled`. Otherwise enforces `RegistrationMode` (Simple / SecretWord / Disabled). |
| `POST /exchange-profile-token` | Returns the narrower `profile_token`. |
| `POST /forgot-password` | Body: `{ email }`. Always returns 204 (no enumeration leak). Throttled 3/hour/email via `IMemoryCache`. Silently no-ops if email is disabled. |
| `POST /reset-password` | Body: `{ token, newPassword }`. Returns 204 on success, 400 with a generic "Invalid or expired" message otherwise. |
| `POST /invitations/validate` | Body: `{ token }`. Returns `{ email, expiresAt }` for a valid invite, 404 otherwise. |

Admin-only endpoints (`AdminOnly` policy):

| Route | Notes |
| --- | --- |
| `POST /invite-code` | Legacy: creates a 3-word `RegistrationTicket` (shared, anyone can use). |
| `GET /invitations` | Lists outstanding `InvitationTicket` rows (per-email, single-use, hashed token). |
| `POST /invitations` | Creates a per-email invite, sends `AdminInvite` email. Returns 400 if email is disabled, 409 if the email already maps to an account. |
| `DELETE /invitations/{id}` | Revokes an invite. |

## Registration modes vs invitations

There are two parallel invitation systems and they coexist:

- **Legacy `RegistrationTicket`** (3-word shared codes). Used when `RegistrationMode == SecretWord`. Anyone with the code can register; codes are consumed on use. `AuthManager.GenerateInviteCodeAsync` creates them; surfaced on the **Users & Access** admin page.
- **Per-email `InvitationTicket`** (Phase 4 email invitations). Independent of `RegistrationMode` — works regardless of the mode setting, including `Disabled`. Each invite is tied to one email address, requires the recipient's email to match at registration, and is consumed on use. Surfaced on the **Email Invitations** admin page. See `docs/email.md`.

`AuthManager.RegisterAsync` checks `inviteToken` first; if present, it takes the invitation path. Otherwise it falls through to the legacy `RegistrationMode`-based flow.

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
| `playback_prefs_<profileId>_<deviceId>` | Per-profile/device bandwidth + max resolution + max audio channels JSON. Mirrored server-side via `profileDeviceSettingsService`. | `SettingsPage` (Playback tab) |
| `iptv_prefs_<profileId>_<deviceId>` | Per-profile/device IPTV provider selection + timeshift prefs JSON. Mirrored server-side via `profileDeviceSettingsService`. | `SettingsPage` (Providers tab) |
| `vora_library_migration_job_id` | Active library-migration job id. Lets the admin reload `/admin/library-migration` and re-attach to the running job. Cleared on "Run again" / "Start over" or when the backend returns 404 (job evicted on server restart). | `LibraryMigrationPage` |
| Server vault keys | Managed by `utils/serverVault.ts` (`VAULT_KEY`, `ACTIVE_SERVER_KEY`) | `serverVault` |

**`is_admin` is dead.** It is never written by any current code. If you find code reading `is_admin`, replace it with `is_server_admin`.

`sessionStorage` is used for **pending pre-vault** state during the login/setup flow (`pending_server_url`, `pending_user_token`, `pending_user_id`, `pending_server_name`).

## Live permission refetch

The profile JWT carries permission claims (`canTimeshiftIptv`, `canRecordLiveTv`, etc.) at the moment the user signed in. If an admin edits those permissions afterwards, the JWT is stale until the user re-signs-in or switches profile.

Players that gate UI on those permissions (e.g. `LiveTvPlayer`'s skip-back/forward and record buttons) read the JWT for the initial render, then immediately call `userService.getUserAccount(userId)` to refresh from the server and overwrite the stale value. New player surfaces that gate on user-level permissions should follow the same pattern — JWT for the first paint, server fetch for the truth.

## Sign-out

`authService.logout()` clears `account_token`, `profile_token`, `user_id`, `profile_name`, `is_server_admin`, `is_profile_admin`, and the entire `sessionStorage`. It does **not** clear `device_id` — that key persists across logouts so the user's favorites/preferences stay attached to the same device record.

`MainLayout.handleSignOut` also calls `serverVault.clearVault()` to drop the multi-server vault, then redirects to `/login`.

## Device tracking

Every authenticated request goes through `Vora.Api/Middleware/DeviceTrackingMiddleware.cs`. It:

1. Reads the **`X-Vora-Device-Id`** header (mandatory — no header = no tracking, request continues anyway).
2. Looks up the existing `ClientDevice` row by `DeviceId`. If blocked, returns 403.
3. If new, inserts a row using these companion headers (all sent by `api/client.ts`):
   - `X-Vora-Client` → `ClientName` (≤64)
   - `X-Vora-Device` → `DeviceName` (≤128)
   - `X-Vora-Device-Type` → `DeviceType` (≤32)
   - `X-Vora-OS` → `OperatingSystem` (≤64). The frontend sends a parsed short name (`Windows 10/11`, `macOS`, `iOS`, …) via `detectOs()` in `client.ts` — **never** the raw `navigator.userAgent` (overflows the column).
4. Updates `LastConnectedAt`, `LastIpAddress`, `LastUserId`, `LastProfileId`. Geo-looks-up the IP via the named HttpClient `DeviceTrackingMiddleware.GeoLookupHttpClientName` only when the IP changes.
5. Caches the device for 5 minutes to avoid re-upserting on every request.

`DeviceEndpoints.UpdateCapabilitiesAsync` (`PUT /api/devices/capabilities`) reads the same `X-Vora-Device-Id` header to attach codec/container/audio-channel capabilities to the device row. `StreamingEndpoints` start-session does the same.

**Header naming is universal: `X-Vora-Device-Id`.** Older code may reference `X-Device-Id`; treat any sighting as a bug and fix it.

### Device identity durability & native clients

`DeviceId` is an **opaque string** — the middleware upserts on whatever the client sends, so identity is entirely the client's choice of value; the server needs no change to accept a different kind of id.

- **Web** can't read any hardware identifier (browsers block MAC/serial for privacy), so it uses a generated UUID. That id is only as durable as browser storage, hence the `vora_device_id` cookie backup above. A full site-data clear or a different browser is still a new device row — an inherent web limitation.
- **Native clients** should send a **platform-stable device id** as `X-Vora-Device-Id` instead of a random UUID — Apple `identifierForVendor`, an app-scoped stable id on Android (e.g. a UUID persisted in app storage, optionally seeded from `Settings.Secure.ANDROID_ID`). These survive reinstalls/cache-clears far better than web storage, so a physical device maps to one `ClientDevice` row. No MAC anywhere — Apple and Google both hide/randomize it. Native clients send the same companion `X-Vora-*` headers (Client / Device / Device-Type / OS) as the web client.

## Per-profile data preservation across purge

Per-profile ratings + watch-state don't die with a purged media item. When an item is permanently removed (Media Trash auto-purge or manual `DELETE /media/trash/{id}` — see `docs/scanning-and-tasks.md`), each profile's rating and watch-state is first archived into the `PreservedUserMediaData` table, keyed by a **content identity** rather than the item's `MediaItem.Id`:

- `ContentIdentity.Compute` (`Vora.Application/Media/ContentIdentity.cs`) builds a stable key from external ids — movies/shows as `type:tmdb|imdb|tvdb:<id>`, episodes as `episode:<series id>:<season>:<episode>`.
- When the same content is later re-added (a different `MediaItem.Id`, same content key), the archived rating + watch-state is restored to each profile.

This is why removal is non-destructive to user data even though the row's GUID changes on re-add. The table schema is in `docs/database.md`.

## Multi-server clients

`utils/serverVault.ts` stores an array of connected `VoraServer` records (id, name, url, token, profileId, profileName). The active server is tracked separately. `apiClient` calls accept `{ serverId }` to target a non-active server. `ServerManagerModal` is the UI for adding/switching/removing servers.

Profile tokens are scoped to a server. When the user signs out of one server but stays on others, only that server's vault entry is removed.
