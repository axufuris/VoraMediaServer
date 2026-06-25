# Vora Deep-Dive Review #2 — 2026-06-25

Second full pass, targeting angles the first review under-covered: **authorization/security**, the **data layer (EF Core)**, and a **broad frontend sweep** (security, async correctness, error states). Findings below are verified against source; the security IDOR cluster was confirmed by hand.

## Security & authorization

### HIGH — Broken object-level authorization (IDOR), one with privilege escalation

A shared root cause: several non-admin endpoints act on a resource id taken from the path/route without verifying the resource belongs to the calling account/profile. All sit on `RequireAuthorization()` groups (any authenticated caller).

**1. `Vora.Api/Endpoints/ProfileEndpoints.cs:118,226` — profile permission escalation (most serious).**
`PUT /api/users/profiles/{profileId}` → `UpdateProfileAsync` passes `profileId` straight to `UpdateManagedProfileAsync` with no ownership/admin check, and the DTO sets the full permission set: `HasAllLibraryAccess`, `AllowedMovie/Tv/MusicRatings`, `BlockUnratedContent`, `AllowedLibraryIds`, `HasAllIptvAccess`, `AllowedIptvPlaylistIds`, `AccessSchedules`, and `Pin`. Any authenticated profile can therefore grant **itself** full library access, clear its content-rating limits, remove its PIN, or rewrite any other profile on any account — fully bypassing parental controls and per-profile library/IPTV gating. `CreateProfileAsync` (`{userId}`), `DeleteProfileAsync`, and `ValidatePinAsync` share the same no-ownership-check shape.
*Fix: requires a decision on the intended model — permission fields almost certainly admin-only; name/image/PIN account-owner-scoped (verify the target profile's `UserId == GetAccountId()`).*

**2. `Vora.Api/Endpoints/ProfileEndpoints.cs:143-194,298-356` — per-profile/device prefs IDOR.**
The `{profileId}/radio-prefs` and `/api/users/profiles/{profileId}/devices/{deviceId}/*` (nav, iptv, radio, discovery-layout, home-layout, settings) read/write any path-supplied profile's preferences with no ownership check. The parallel `me/*` routes are correctly scoped — these id-in-path variants are not.
*Fix: verify the profile belongs to the caller's account, or drop the id-in-path variants in favor of `me`.*

**3. `Vora.Api/Endpoints/ProviderEndpoints.cs:21-47` — external-provider OAuth token IDOR.**
`/api/users/{userId}/providers` GET/POST/DELETE operate on the path `userId` with only `RequireAuthorization()`. A user can list, overwrite, or delete another account's linked-provider access/refresh tokens.
*Fix: assert `userId == GetAccountId()` (or admin) before the manager call.*

**4. `Vora.Api/Endpoints/DvrEndpoints.cs:25,112,118` — DVR recordings IDOR.**
`GET /sessions/{profileId}` returns any profile's recordings; `DELETE /sessions/{sessionId}` and `DELETE /series/{sessionId}` resolve by id in `DvrManager` (`DeleteRecordingAsync`/`CancelSeriesAsync`, lines 166/185) with no owning-profile check. Any authenticated user can view, delete, or cancel another user's recordings/series.
*Fix: thread `GetProfileId()`/account through and verify `session.ProfileId` belongs to the caller before listing/deleting/cancelling.*

### MEDIUM

**5. `Vora.Api/Endpoints/MusicEndpoints.cs:175` — admin music group not policy-gated.** The `/api/admin/music` group has no `.RequireAuthorization("AdminOnly")`; it relies on a manual `if (!user.IsAdmin())` in each handler. Both current handlers check, but any future handler added to the group inherits no admin gate. *Fix: add `.RequireAuthorization("AdminOnly")` to the group like every other admin group.*

**6. Raw `ex.Message` returned to clients on IO/parse paths** — e.g. `PodcastEndpoints.cs:84,152` ("Failed to fetch or parse feed: {ex.Message}"), `PluginEndpoints.cs:50,65`. Can leak internal hostnames/paths. Most other `ex.Message` returns are intentional domain-validation messages (fine). *Fix: generic message for the network/IO/parse catch-alls.*

### LOW

**7. SSRF on server-side fetches of configured URLs** — IPTV playlist/EPG and channel passthrough (`IptvManager`/`IptvEpgService`/`IptvPassthroughService`) and podcast feeds (`PodcastManager.cs:471`). IPTV URLs are admin-configured (low risk); the **podcast feed fetch is reachable by non-admin profiles** with `canAddCustomPodcastFeeds`, so it's the one worth hardening — no allow-list / private-IP block, so a feed URL pointing at `169.254.169.254` or internal hosts would be fetched. *Fix: block loopback/link-local/private resolution on the podcast fetch first.*

### Confirmed clean
JWT validation (issuer/audience/lifetime/signing, min-secret-length, loud dev fallback); the `AdminOnly` policy is applied consistently across all other admin groups; all file-serving endpoints route through `SafePathResolver` containment and are signed-token-gated; the music stream token fix from review #1 verified correct; playlists/smart-playlists are profile-scoped in the manager; no secrets logged.

## Data layer (EF Core)

### HIGH (clean wins — one migration each)
**8. `VoraDbContext.cs:216-244` — `MediaItem` has no index on `LibraryId` or the `MediaType` discriminator,** yet nearly every query filters on one/both (library browsing, `OfType<Movie>()`, music via Track/Album, marker coverage). Sequential scans on large libraries. *Fix: add `HasIndex(LibraryId)` + a composite `{ LibraryId, MediaType }`; add a migration.*

**9. `VoraDbContext.cs:1108` / `StreamRepository.cs:53-107` — the now-playing/dead-session queries filter `EndedAt == null && LastPingAt >= cutoff`,** but the only `StreamSession` index is `{ UserId, StartedAt }`. Every now-playing poll and the dead-session reaper scan all open sessions. *Fix: `HasIndex({ EndedAt, LastPingAt })` (or filtered on `LastPingAt WHERE EndedAt IS NULL`).*

### MEDIUM
**10. `MusicRecommendationRepository.cs:93-106` `GetTopArtistsForProfile`** loads every play-history row for the profile into memory, then groups + applies exponential-decay scoring in C#. *Fix: pre-aggregate per-artist counts + `Max(PlayedAt)` in SQL, apply decay over the small grouped set.*

**11. `MediaRepository.cs:156-172` `GetForMetadataSyncAsync`** chains ~10 collection `.Include()`s on one tracked query with no `AsSplitQuery()` — cartesian explosion. *Fix: `.AsSplitQuery()`.*

**12. `UserMediaStateRepository.cs:189-336` `GetContinueWatching`** does the final `OrderByDescending(LastPlayedAt).Take(limit)` in C# after fetching all in-progress movies and all candidate episodes across every watched show. *Fix: cap candidate sets in SQL before the in-memory merge.*

**13. `MusicRecommendationRepository.cs:441` `GetYearsWithHistory`** loads every `PlayedAt` to compute distinct `.Year` in memory (Npgsql can translate `.Year`). **`IptvRepository.cs:325` `GetDvrUsageBytes`** stats each file (`File.Exists`+`FileInfo.Length`) in a loop instead of summing the stored `FileSizeBytes` column (which `GetDvrTotalUsageBytes` already does). *Fix: project `.Year` in SQL; use `FileSizeBytes`.*

### LOW
**14. Rating setters** (`MusicRepository` `SetAlbum/ArtistRating`, `UserMediaStateRepository` `SetMediaRating`) do read-modify-write with no concurrency token — last-writer-wins on concurrent admin edits. *Fix: add `xmin` concurrency token if it matters, else accept explicitly.* `IptvRepository.GetAllPlaylists`/`GetPlaylistById` `.Include(Channels)` without split query (thousands of channels) — `AsSplitQuery()`.

### Confirmed clean
`AsNoTracking` discipline consistent; `ExecuteUpdate`/`ExecuteDelete` patterns correct (incl. transactional `ReplaceMarkers`); no `SaveChanges`-in-loop; history/admin queries properly paginated with SQL-side count; smart-playlist string rules stay server-side (`ToLower`).

## Frontend

### HIGH (UX/correctness — double-submit & silent failure)
**15. `pages/Auth/RegisterPage.tsx:228` and `pages/Auth/SetupPage.tsx:86`** — submit buttons have no `disabled`/loading guard; double-click can create duplicate accounts / double-submit the first-run server claim. `LoginPage`/`ResetPasswordPage` already do this correctly. *Fix: `isSubmitting` state + `disabled`.*
**16. `pages/Admin/SmartLists/SmartListsPage.tsx:112-134`** — `handleSubmit` has no try/catch and no saving guard; on failure the modal stays open with no feedback, and double-clicks double-submit. *Fix: try/catch → `dialog` error + `saving` flag.*

### MEDIUM
**17. `pages/Admin/Libraries/ManageLibrary.tsx:582-610`** — hand-rolls its own alert/confirm modals (convention violation — should use `useDialog`) AND both overlays are `z-50`, rendering **behind** the `z-[100]` header. *Fix: replace with `dialog.alert/confirm`.*
**18.** A few admin action buttons (RequestsPage approve, OverlayEditor save) aren't disabled while their async runs — duplicate-action risk; worth confirming exact lines. 

### LOW
**19.** `Promise.all(...).catch(console.error)` in several admin loaders (SettingsPage, SmartListsPage, DashboardPage) — one rejection drops the whole batch with only a console line, no UI signal. *Fix: `Promise.allSettled` or visible error state.* Index-as-key in mutable lists (folder-path editors, dialog stack) — use stable ids.

### Confirmed clean
No `dangerouslySetInnerHTML` (no XSS surface); no `alert/confirm/prompt` except the one hand-rolled ManageLibrary case; no direct axios in pages; TypeScript safety excellent (no `any`/`@ts-ignore` in prod); token handling solid (Bearer header only, never in URLs/logs, cleared on 401); the `?invite=` token is by-design.

## Suggested priority
1. **The IDOR cluster (#1–#4)** — security; #1 (profile permission escalation) is the most serious and needs a decision on the intended authorization model.
2. The two missing indexes (#8, #9) — one migration, broad performance payoff.
3. Admin-group gate (#5), double-submit guards (#15, #16), ManageLibrary modal (#17).
4. The data-layer over-fetch queries (#10–#13) and remaining LOW items.
