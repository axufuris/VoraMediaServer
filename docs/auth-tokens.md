# Tokens, refresh and sign-in limits

What a sign-in produces, what each token carries, and when one stops working. The pages, endpoints, storage keys and device tracking are in [`docs/auth-and-devices.md`](auth-and-devices.md).

## The three tokens

| Token | Returned as | Lifetime | What it is |
| --- | --- | --- | --- |
| Account token | `accessToken` from `/auth/login`, `/auth/register`, `/auth/setup`, `/auth/refresh` | 2 hours | HS256 JWT. Claims: `sub` and `accountId` (the account id), `stamp`, `type: account_level`. Nothing about libraries, ratings or permissions. |
| Profile token | `token` from `/auth/exchange-profile-token`; `profileToken` from `/auth/refresh` when a `profileId` was sent | 7 days | HS256 JWT for one profile. The token nearly every request carries. |
| Refresh token | `refreshToken` from login, register, setup and refresh | 90 days | 48 random bytes, base64url. Not a JWT: only its SHA-256 is stored (`AuthRefreshTokens`), with the family id, the account's security stamp, the device id and the expiry. |

The profile token's claims: `sub` (the profile id), `accountId`, `isAdmin` (the **account's** server-admin flag), `stamp`, `profileStamp`, `hasAllLibraryAccess`, `allowedLibrary` (repeated, only when access is limited), `hasAllRatings`, `blockUnrated`, `allowedMovieRating` / `allowedTvRating` / `allowedMusicRating` (repeated, only when ratings are limited), `canTimeshiftIptv`, `canRecordLiveTv`, `canAddCustomPodcastFeeds` and `accessSchedules` (a JSON string). Read them only through `AuthExtensions`.

`AdminOnly` requires the claim `isAdmin` = `True`, so an account token never passes it; only a profile token of an admin account does. Any other valid token of the account can call `/auth/exchange-profile-token` (the Android profile switcher does it with the live profile token).

Signing: `Jwt:SecretKey` (env `Jwt__SecretKey`), `Jwt:Issuer` and `Jwt:Audience` (both `VoraMediaServer` in `appsettings.json`). Outside Development and Testing the server refuses to start with a missing or placeholder key or one under 32 UTF-8 bytes. Changing the key signs everyone out.

## What makes a valid token

The bearer handler checks signature, issuer, audience and lifetime. Then every request compares the token's `stamp` with the account's current `User.SecurityStamp` and, for a profile token, `profileStamp` with `UserProfile.SecurityStamp`. A mismatch or a missing stamp is a 401. Both stamps are cached in memory for 30 seconds. SignalR (`/hubs/Vora`) accepts the token as the `access_token` query parameter.

The stamps rotate, so every token and refresh token of that account or profile dies:

- **Account stamp:** a password change in account settings or through a reset, an email change, an admin editing the account's access and permissions (`PUT /users/{id}/access`).
- **Profile stamp:** any edit of that profile (`PUT /users/profiles/{id}`).

Permissions are therefore fixed when a token is issued, and an admin edit makes the old token fail within about 30 seconds instead of leaving it stale. The hub also sends `UserAccessUpdated` / `ProfileAccessUpdated` so a client can tell the viewer.

`accessSchedules` is data only: the server never checks it. The web client reads it in `MainLayout` and shows a locked screen outside the profile's windows. Android, Roku and tvOS edit schedules but do not enforce them.

## Refresh and sign-out

`POST /auth/refresh { refreshToken, profileId? }` is anonymous and reads `X-Vora-Device-Id`. It returns a new account token and a **new** refresh token in the same family with another 90 days; the presented one is revoked. With a `profileId` it also returns `profileToken`, left empty for a PIN-protected profile (the client asks for the PIN and exchanges). It answers 401 for an unknown or expired token. Presenting a token that was already revoked (a replay, or one logged out) revokes the whole family, and so does a refresh whose stored stamp no longer matches the account.

`POST /auth/logout { refreshToken }` revokes the family and always answers 204. A new login from a device revokes that device's earlier refresh tokens first. Rows are kept, revoked or not.

Android, Roku and tvOS refresh once on a 401, retry the request, and only then treat the session as expired. The web client (and Vora Desktop, which loads it) never calls `/auth/refresh` or `/auth/logout`: it ignores `refreshToken`, so its session is the 7-day profile token, and any 401 clears the tokens and goes to `/login`.

## Passwords, lockout and rate limits

- Passwords are BCrypt hashes. At least 8 characters on setup, registration, admin-created accounts and a reset. Changing the password in account settings is not length-checked by the server.
- **Lockout:** per typed email (lower-cased, in memory), 5 failed sign-ins within 15 minutes lock that email for 5 minutes. A locked attempt answers 401 like a wrong password. A success clears the count; a restart clears everything.
- **Rate limits** per client address, fixed window, answering 429. `AuthStrict` is 10 requests per 5 minutes: login, register, forgot-password, reset-password, confirm-email-change, validate-pin. `AuthBurst` is 30 per minute: refresh, logout, exchange-profile-token, invitations/validate. `setup` has none.

## Profile PIN

A PIN is stored as a BCrypt hash. Older profiles may hold an unsalted SHA-256 (base64), which `ProfilePin.Verify` still accepts with a fixed-time compare. `POST /auth/exchange-profile-token` takes an optional `{ "pin": "1234" }` and checks it for a profile that has one. A missing or wrong PIN is **403** (ProblemDetails titled "PIN required" or "Incorrect PIN"), never 401, which every client reads as an expired session. `POST /users/profiles/{id}/validate-pin` answers `200 { "valid": true }` or the same 403; Android, Roku and tvOS call it before the exchange, the web does not. The exchange and refresh, not the clients, decide: `AuthManager.GenerateProfileTokenAsync` returns a `ProfileTokenResult` that says whether the PIN was missing or wrong.
