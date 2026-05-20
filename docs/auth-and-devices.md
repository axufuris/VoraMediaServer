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

- `pages/Auth/LoginPage.tsx` — Email + password only. Derives the server URL from `import.meta.env.VITE_API_BASE_URL` or falls back to `window.location.origin` and probes it on mount. **No** server URL input shown to the user.
- `pages/Auth/RegisterPage.tsx` — Same approach: probe the current-origin server, then show the register form (or "registration disabled" / "needs invite code" depending on the server's `registrationMode`).
- `pages/Auth/SetupPage.tsx` — First-run server-claim flow.
- `pages/Profile/ProfileSelectionPage.tsx` — Picks a profile after account-token login, exchanges for a `profile_token`.

When the user adds **additional** servers, that flow lives in `components/Layout/ServerManagerModal.tsx` — which **does** show a server URL field (because the additional server is by definition not the current origin).

## localStorage keys

These are the canonical keys. Don't invent new ones in a vacuum.

| Key | Purpose | Set by |
| --- | --- | --- |
| `device_id` | Per-browser stable UUID. Created in `App.tsx` on first load if missing. **Survives logout.** | `App.tsx` |
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
| `vora_show_spotlight_<profileId>` | Per-profile toggle for the Home page spotlight hero. Default `true`. Setting this dispatches a `vora:home-prefs-changed` window event so `HomePage` re-reads it live. | `SettingsPage` (Templates tab) |
| `playback_prefs_<profileId>_<deviceId>` | Per-profile/device bandwidth + max resolution + max audio channels JSON. Mirrored server-side via `profileDeviceSettingsService`. | `SettingsPage` (Playback tab) |
| `iptv_prefs_<profileId>_<deviceId>` | Per-profile/device IPTV provider selection + timeshift prefs JSON. Mirrored server-side via `profileDeviceSettingsService`. | `SettingsPage` (Providers tab) |
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

## Multi-server clients

`utils/serverVault.ts` stores an array of connected `VoraServer` records (id, name, url, token, profileId, profileName). The active server is tracked separately. `apiClient` calls accept `{ serverId }` to target a non-active server. `ServerManagerModal` is the UI for adding/switching/removing servers.

Profile tokens are scoped to a server. When the user signs out of one server but stays on others, only that server's vault entry is removed.
