# Vora Deep-Dive Review #3 — 2026-06-25

Third pass, targeting ground the first two reviews did not cover: **config/middleware/deployment**, **auth-flow depth**, the **email/backup/upload/SignalR subsystems**, and the **domain layer** — plus a regression-verification sweep over the changes made earlier this session. Security findings were hand-verified.

Two findings are already **FIXED** in this pass (critical + high). The rest are catalogued with severities for triage.

## FIXED in this pass

**[CRITICAL] Unauthenticated profile-token minting → account takeover — `AuthEndpoints.cs:59` / `AuthManager.GenerateProfileTokenAsync`.**
`POST /api/auth/exchange-profile-token?accountId=&profileId=` sat on the anonymous `/api/auth` group; it took both ids from the query and `GenerateProfileTokenAsync` only checked the profile *exists under that account* — never that the caller owned it. Since account/profile GUIDs are returned in normal API responses, anyone could mint a full 7-day profile JWT (with `isAdmin`, library, and rating claims) for any profile, bypassing both password and PIN. **Fix applied:** added `.RequireAuthorization()` and the handler now derives the account from the caller's JWT (`GetAccountId()`) and rejects a mismatched query `accountId`. Verified the web client already sends the account token on both call sites, so no flow breaks.

**[HIGH] Swagger/OpenAPI exposed in all environments — `WebApplicationExtensions.cs:18`.**
`UseSwagger()` + `UseSwaggerUI()` ran unconditionally, publishing the full API surface at `/swagger` to unauthenticated callers in Production. **Fix applied:** wrapped both in `if (app.Environment.IsDevelopment())`. The OpenAPI codegen workflow runs against dev instances (which default to Development per the project rule), so it's unaffected.

## HIGH — open (needs your input)

**1. Profile PIN is brute-forceable and weakly hashed — `ProfileEndpoints.cs:113` (validate-pin), `UserManager.cs:63`, `HashPin` (unsalted SHA-256).**
`validate-pin` has no rate-limit policy (every other auth route does), the PIN space is 4 digits (10k), the compare is ordinary `==` (not constant-time), and PINs are hashed with plain unsalted SHA-256 (rainbow-tableable if the DB leaks). Note: with the exchange-token bypass now fixed, the PIN is reachable only with valid account credentials — but it's still the parental-control gate and is trivially exhaustible. *Fix: add a strict rate-limit (and/or per-profile lockout), `CryptographicOperations.FixedTimeEquals`, and salt the PIN hash (bcrypt/Argon2 or per-profile salt).*

## MEDIUM — open

**2. No request-body / multipart size limits anywhere** (no `MaxRequestBodySize`/`MultipartBodyLengthLimit` in the codebase). Upload endpoints stream `Form.Files` uncapped → trivial memory/disk DoS. *Fix: global Kestrel `MaxRequestBodySize` + per-upload limits.*

**3. No account lockout on login — `AuthManager.LoginAsync`.** Brute-force resistance is only the per-IP rate limiter; a distributed/rotating-IP attacker faces no per-account ceiling. *Fix: per-account failed-attempt counter with backoff/lockout.*

**4. Self-service email change is unverified — `UserManager.UpdateUserAccountAsync`.** A user can set their account email to any value (lowercased only; no confirmation, no uniqueness check, and it does **not** rotate the security stamp, so other sessions persist). Email is the login id and reset target. *Fix: confirmation-link verification, uniqueness enforcement, rotate stamp on change.*

**5. Upload content is not validated — `ArtworkService.cs:67`, `UserProfileImageService.cs:44`, `MusicEndpoints` upload.** Extension is taken from user-supplied `file.FileName`; no magic-byte check, no decompression-bomb guard. Serve-side is safe (regex + `SafePathResolver`), so the risk is accepting non-images + unbounded decode, not stored XSS. *Fix: validate leading magic bytes against the allowed image set; derive the stored extension from the detected type.*

**6. Secrets stored unencrypted in backup zips — `SettingsSections.cs` (DataProtection keys + `SmtpPasswordCiphertext`).** Anyone with read access to the backups dir or an exfiltrated zip can decrypt the SMTP password. Download endpoint is correctly `AdminOnly`; this is at-rest exposure. *Fix: encrypt backup archives at rest, or document that the backups dir must be access-controlled.*

**7. Artwork/collection/music-artwork upload is any-authenticated, not admin — `ArtworkEndpoints.cs:26`, `CollectionArtworkEndpoints.cs:17`, `MusicEndpoints.cs:48`.** Any profile can overwrite artwork on any media/collection/artist/album. *Fix: confirm intended; if curation is admin-only, add the `AdminOnly` policy.*

**8. CORS combines `AllowCredentials()` with `AllowAnyHeader/Method` — `ServiceRegistrationExtensions.cs:776`.** Origins are an explicit allow-list (good), but the app is bearer-token based so `AllowCredentials()` is unnecessary and broadens the policy; dev localhost origins also ship in Production. *Fix: drop `AllowCredentials()`; don't seed dev origins outside Development.*

**9. Content-Security-Policy — PARTIAL (2026-06-25).** Added a conservative CSP to `SecurityHeadersMiddleware.cs`: `frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'` (clickjacking/base-tag/plugin/form protections that don't risk breaking the app). **FUTURE ENHANCEMENT (worthwhile):** add a `script-src`/`default-src` policy — this is the real XSS mitigation but it's the part most likely to break the SPA, so it must be developed iteratively against the running app (console open, adding sources until HLS playback, the YouTube iframe, SignalR/websockets, and remote artwork all work), ideally first deployed `Content-Security-Policy-Report-Only` to catch violations before enforcing. Deferred deliberately until someone can test it end-to-end.

## LOW — open

10. **Rate-limit partition is RemoteIp only** (`ServiceRegistrationExtensions.cs:177`); behind a proxy without `ForwardedHeaders` (currently disabled) all clients share the proxy IP → the auth limiter collapses to one global bucket. *Fix: enable forwarded headers with `KnownProxies` in proxied deploys; document it.*
11. **SSRF guards have a DNS-rebinding TOCTOU** (podcast `EnsureFeedUrlIsSafeAsync`, `SafeImageDownloader`): resolve-then-connect re-resolves; also CGNAT `100.64/10` and benchmark ranges aren't blocked. *Fix: pin the validated IP via `SocketsHttpHandler.ConnectCallback`, or disable auto-redirect + re-validate.*
12. **Invite *code* (SecretWord) is a 4-digit PIN, 30-min expiry, no attempt limit** (`AuthManager.cs:64`). The invitation-*token* path already uses 32 random bytes. *Fix: mirror the token path (longer alphanumeric).*
13. **Admin "block device" is keyed on the client-supplied `X-Vora-Device-Id`** (`DeviceTrackingMiddleware`), so it's trivially evaded by changing the header (no auth decision depends on device id — informational only). *Fix: treat as best-effort or bind device id to account.*
14. **`/invitations/validate` echoes the invited email to an unauthenticated caller** — minor enumeration. Rate-limited. *Fix: consider not echoing the email.*
15. **Docker hardening — `docker-compose.yml`** lacks `no-new-privileges`, `cap_drop`, a healthcheck, and a digest-pinned base; the Dockerfile already runs non-root (good). FFmpeg in-container = large RCE blast radius. *Fix: add the hardening directives.*
16. **Domain — `EmailTemplate.UpdatedAt` / `EmailDeliveryLog.CreatedAt` lack a `= DateTime.UtcNow` default** (every other audit column has one); PostgreSQL `timestamptz` rejects unset `DateTime.MinValue`. Confirm the write path always sets them. *Fix: add the UTC default to match.*

## Confirmed clean
Password hashing is bcrypt with per-hash salt; reset & invitation **tokens** are 32 random bytes, stored SHA-256-hashed, server-side expiry, single-use, and reset rotates the security stamp; reset returns 204 regardless of email existence (no enumeration); invites are email-bound and create non-admin accounts; no self-service admin elevation. SMTP/header injection blocked (CRLF rejection + MailKit encoding); email HTML values are `HtmlEncode`d; backup restore has no zip-slip (fixed internal entry paths + `Path.GetFileName` sanitization) and the download/restore endpoints are `AdminOnly`; **SignalR** hub is `[Authorize]`, groups are derived server-side from JWT claims (clients can't join arbitrary groups), admin streams gated to the `admins` group. JWT secret validation, DataProtection key persistence, global exception handler (no prod stack-trace leak), and Dockerfile non-root all solid. Antiforgery-disabled uploads are safe (bearer-token, no cookies).

## Regression verification — this session's changes
All verified correct by re-reading: `DvrRecordingService` (no lock-across-await, no double-dispose, no kill/CTS race), `TranscodeJanitorWorker` + evict/reap (no `SemaphoreSlim` re-entrancy), `BuildFFmpegArguments` token split (no mis-split; all paths discrete), `AccountOwnershipFilter` (no bypass; DVR sessionId routes correctly covered by the separate `CallerOwnsSessionAsync` check), `GetContinueWatching` split (equivalent output), `DiscoveryManager.EnrichWithStatus` (mixed types + null ExternalId handled), `EnsureFeedUrlIsSafeAsync` (IPv4/IPv6 private/loopback/link-local all covered; only the TOCTOU/CGNAT gaps in #11).
