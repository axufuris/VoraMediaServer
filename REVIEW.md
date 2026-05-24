# Vora — Full Project Review

_Generated: 2026-05-23. Reviewer scope: backend architecture, data architecture, frontend, dead code, cross-cutting (security/performance/observability/resilience), build & deploy, documentation._

This document is a single consolidated review of the Vora codebase. Findings are prioritized by severity and grouped so you can attack them in roughly the order they appear. Every finding includes the file (and line numbers where possible) so you can jump straight to it.

Severity legend:

- **CRITICAL** — security, data loss, or correctness bug. Fix before any production deployment.
- **HIGH** — significant architectural problem, perf foot-gun, or design smell that will compound.
- **MEDIUM** — should be fixed, but not bleeding.
- **LOW / NIT** — polish, consistency, minor refactor.

Counts at a glance:

| Bucket | CRITICAL | HIGH | MEDIUM | LOW / NIT |
|---|---|---|---|---|
| Backend architecture | 7 | 7 | 12 | 15+ |
| Data architecture | 0 | 9 | 20 | 12 |
| Frontend | 1 | 11 | 11 | 11 |
| Dead code | — | — | — | 34 items |
| Cross-cutting (security/perf/etc) | 6 | 14 | 13 | 9 |

---

## 1. Executive summary

The codebase is well-structured at the macro level. Layer separation, an explicit plugin system, manager-pattern endpoints, well-organized docs in `docs/`, and a sensibly composed React SPA are all in place. CLAUDE.md is unusually disciplined and clearly enforced.

That said, the review surfaced a non-trivial cluster of things to address. The biggest themes are:

1. **Security gaps in the perimeter.** The JWT signing key default is a literal placeholder string, several streaming endpoints serve files without auth or path-traversal protection, and the SignalR hub accepts anonymous connections and broadcasts to `Clients.All` even for per-profile data. Combined with `ASPNETCORE_ENVIRONMENT=Development` baked into `docker-compose.yml`, a default deployment is wide open.
2. **No automated tests at all.** No `*.Tests.csproj`, no Vitest/Jest in the SPA. With the codebase past 165k LOC, this is the single highest-leverage thing to fix.
3. **A handful of fire-and-forget `Task.Run` calls capture scoped services and will throw `ObjectDisposedException` under load.**
4. **Project layer boundaries documented in CLAUDE.md are not fully enforced.** `Vora.Application` references `Vora.Plugins`, and `Vora.Domain` pulls in `Pgvector.EntityFrameworkCore`.
5. **Several data-architecture wins are easy and high-impact:** add an HNSW index on the embeddings vector, add indexes on the external-id columns, switch to `AddDbContextPool`, and replace the `.ToLower().Contains(...)` search pattern with `EF.Functions.ILike`.
6. **The frontend's biggest problem is `PlayerContext`.** The provider's value object is rebuilt every render and rerenders every consumer four times per second during playback. Memoizing it (and splitting the time slice into its own micro-context) will improve perf across the entire app.
7. **Color tokens are violated in 39 files** — most importantly in the shared `Modal`, `Dialog`, and `MediaCard` primitives, which means the template-driven brand recolor doesn't actually work end-to-end.

The "must do" shortlist is in section 10 at the end.

---

## 2. Critical issues — fix first

These are the items that warrant immediate attention.

### 2.1 JWT signing key defaults to a placeholder string

- **File:** `src/Vora.Api/appsettings.json:7`
- The committed `Jwt:SecretKey` is the literal `REPLACE_THIS_WITH_A_VERY_LONG_AND_SECURE_RANDOM_STRING_IN_PRODUCTION!`. `AuthManager.CreateToken` (`src/Vora.Application/Auth/AuthManager.cs:267`) throws only when the key is missing — not when it's still the placeholder. A deployment that forgets to override it lets anyone forge tokens for any account or profile, including admin.
- **Fix:** At startup, fail fast if `Jwt:SecretKey` equals the placeholder, is null/empty, or is shorter than 32 bytes. Document `dotnet user-secrets` / env-var override in `README.md`.

### 2.2 JWT validation doesn't enforce issuer or audience

- **File:** `src/Vora.Api/Extensions/ServiceRegistrationExtensions.cs:489-494`
- `TokenValidationParameters` sets `ValidateIssuer = false`, `ValidateAudience = false`. The `Jwt:Issuer = "VoraMediaServer"` value in `appsettings.json` is defined but never used. Combined with 2.1 above, any party who happens to know the (placeholder) secret can sign tokens for any tenant.
- **Fix:** Set `ValidIssuer` and `ValidAudience`, and add both when signing in `AuthManager.CreateToken`.

### 2.3 No JWT refresh or revocation; admin demotion is delayed

- **File:** `src/Vora.Application/Auth/AuthManager.cs:48, 252`
- Profile tokens live 7 days. Admin status, library access, parental controls, etc. are baked into JWT claims at issuance time and only re-checked at expiry. There's no `tokenVersion`/`securityStamp` claim, no jti blocklist, no rotating refresh endpoint.
- **Impact:** An admin demoted from admin retains admin claims for up to 7 days.
- **Fix:** Add a `securityStamp` claim, bump it whenever an account's privileges change, validate server-side. Alternatively (and per CLAUDE.md's existing pattern for Live TV) re-resolve admin status from the DB on every request via a custom `IClaimsTransformation` or middleware.

### 2.4 DVR recording files served without authorization

- **File:** `src/Vora.Api/Endpoints/DvrPlaybackEndpoints.cs:14-15, 31-40`
- `GET /api/streaming/dvr/file/{sessionId}` and `GET /api/streaming/hls/timeshift/{profileId}/{sessionId}/{fileName}` have no `.RequireAuthorization()`. Anyone who knows or guesses a session GUID streams the file.
- **Fix:** Add `.RequireAuthorization()`, plus an ownership check that the calling profile owns the session. Or use the HMAC pattern from `IptvPassthroughService`.

### 2.5 Path traversal in timeshift HLS endpoint

- **File:** `src/Vora.Api/Endpoints/DvrPlaybackEndpoints.cs:42-67`
- `Path.Combine(tempDir, "timeshift", profileId.ToString(), sessionId, fileName)` is fed to `File.Exists`/`Results.File` with no boundary check on the resolved path. Combined with the lack of auth above, this is reachable by anyone.
- **Fix:** `Path.GetFullPath(...)` + `.StartsWith(rootSep)` containment check; reject `..`/separators in `fileName`. Compare against the safer pattern in `ClientTemplateAssetService.ResolveAssetPath`.

### 2.6 Path traversal in custom artwork + user image endpoints

- **Files:** `src/Vora.Api/Endpoints/ArtworkEndpoints.cs:13, 26-44`; `src/Vora.Api/Endpoints/UserImageEndpoints.cs:15, 40-58` → `src/Vora.Application/Users/UserProfileImageService.cs:34`
- Both endpoints are `.AllowAnonymous()`, both call `Path.Combine(basePath, fileName)` with no traversal protection. Route binding rejects literal `/`, but the lack of containment validation is fragile.
- **Fix:** Validate `fileName` matches an allowlist regex (e.g. `media_<guid>_<kind>_<guid>\.<ext>`), `Path.GetFullPath` + containment check, restrict served extensions.

### 2.7 HLS chunk endpoint anonymous + path traversal

- **File:** `src/Vora.Api/Endpoints/StreamingEndpoints.cs:142-155`
- `GET /api/streaming/hls/{fileName}` — anonymous + `Path.Combine(tempDir, fileName)` with no containment check.
- **Fix:** `RequireAuthorization()` plus containment check.

### 2.8 SignalR hub accepts anonymous connections

- **File:** `src/Vora.Api/Extensions/WebApplicationExtensions.cs:81`
- `app.MapHub<VoraHub>("/hubs/Vora");` — no `.RequireAuthorization()`. The hub class itself has no `[Authorize]`. Anonymous clients connect, get all `Clients.All` broadcasts.
- **Fix:** `app.MapHub<VoraHub>("/hubs/Vora").RequireAuthorization();` and `[Authorize]` on the hub class for defense in depth.

### 2.9 `PUT /api/users/{userId}` has no ownership check

- **File:** `src/Vora.Api/Endpoints/UserEndpoints.cs:48, 89-93` + `src/Vora.Application/Users/UserManager.cs:120`
- The route is gated by group-level `.RequireAuthorization()` only. `UpdateUserAccountAsync` loads the user by id from the URL and overwrites their email, display name, and BCrypt password — with no comparison against the calling principal's account id and no admin check.
- **Impact:** Any authenticated profile can take over any other account by setting a new password.
- **Fix:** In the endpoint, check `user.GetAccountId() == userId || user.IsAdmin()`; return 403 otherwise. Also enforce in the manager.

### 2.10 `Vora.Application` depends on `Vora.Plugins`

- **File:** `src/Vora.Application/Vora.Application.csproj:25`
- CLAUDE.md golden rule: "`Vora.Application` depends only on `Vora.Domain`." But the csproj references `Vora.Plugins`, and Application files import `Vora.Plugins.Interfaces` / `Vora.Plugins.Dtos` throughout (e.g. `MusicManager.cs:11`, `MediaIngestionService.cs:9`).
- **Fix:** Either move the plugin contracts Application needs into `Vora.Application/Contracts`, or update CLAUDE.md to say "Application depends on Domain + Plugins." The de-facto state is the latter; pick one and align.

### 2.11 `Vora.Domain` depends on `Pgvector.EntityFrameworkCore`

- **File:** `src/Vora.Domain/Vora.Domain.csproj:11`
- CLAUDE.md: "Domain depends on nothing else in the solution." Domain has a `PackageReference` to `Pgvector.EntityFrameworkCore`, and `src/Vora.Domain/Entities/Ai/MediaItemEmbedding.cs:7` exposes `Pgvector.Vector` directly.
- **Fix:** Move the EF-adjacent binding out. Keep the embedding as `float[]` / `ReadOnlyMemory<float>` in Domain and translate to `Pgvector.Vector` only in `Vora.Infrastructure`.

### 2.12 Endpoints accept Domain entities as request bodies

- **Files:** `src/Vora.Api/Endpoints/SmartListEndpoints.cs:91, 103` (`[FromBody] SmartList request`); `src/Vora.Api/Endpoints/DiscoveryEndpoints.cs:48` (`[FromBody] List<DiscoveryRowConfig> configs`)
- CLAUDE.md: "API responses must use View Models (`*VM`) or Response classes." Accepting entities is worse — it permits mass-assignment of every property.
- **Fix:** Introduce `SmartListSaveRequest` and `DiscoveryRowConfigRequest`.

### 2.13 Fire-and-forget `Task.Run` captures scoped services

- **Files:** `src/Vora.Application/Iptv/IptvManager.cs:97, 134, 157`; `src/Vora.Api/Endpoints/MusicEndpoints.cs:480-493`
- `_ = Task.Run(() => _epgService.SyncEpgDataAsync(CancellationToken.None));` — `_epgService` is scoped; the request scope is disposed when the endpoint returns. The work inside the Task continues to use a disposed `DbContext`.
- **Impact:** Intermittent `ObjectDisposedException` under load — silently dropped EPG syncs and music-recommendation refreshes.
- **Fix:** Inject `IServiceScopeFactory`, create a scope inside `Task.Run`, OR route the work onto `ITaskQueueManager` which already does scope management.

### 2.14 `docker-compose.yml` sets `ASPNETCORE_ENVIRONMENT=Development`

- **File:** `docker-compose.yml:10`
- Production deployments built from this compose file enable Swagger at root (`RoutePrefix = string.Empty`, `WebApplicationExtensions.cs:17`), the developer exception page (stack traces leak), and any other `IsDevelopment()` shortcut. Combined with 2.1 above this is "fully open admin server in 30 seconds."
- **Fix:** Default to `Production`. Document overriding to `Development` for VS / local runs in `README.md`.

### 2.15 `PlayerContext` rebuilds the provider value every render and rerenders 4x/sec during playback

- **File:** `src/Vora.Web/src/contexts/PlayerContext.tsx:714-726`
- The `<PlayerProvider value={{ ... }}>` object has ~30 fields, is not wrapped in `useMemo`, and contains transport state including `currentTime` which is updated 4× per second via the `timeupdate` listener at line 293. Every consumer of `usePlayer()` rerenders on every tick.
- **Fix:** Wrap in `useMemo`. Split high-frequency state (currentTime/duration) into its own micro-context that only the progress bar subscribes to. Wrap all callbacks in `useCallback`.

### 2.16 Axios instance is rebuilt on every API call

- **File:** `src/Vora.Web/src/api/client.ts:48-79, 94-99`
- `createServerClient` runs at call time, recreates the axios instance, re-attaches request + response interceptors, redoes `detectOs()` regex scanning of the user-agent. No HTTP keep-alive caching benefit, no interceptor reuse.
- **Fix:** Cache instances by `serverId` in a `Map<string, AxiosInstance>` populated lazily.

### 2.17 No automated tests

- **Glob:** `*Tests.csproj` / `*.Tests.csproj` returns zero results. `src/Vora.Web/package.json` has no `vitest`/`jest`/`@testing-library`/`playwright`/`cypress`.
- **Fix:** Stand up at minimum `Vora.Application.Tests` (xUnit + Testcontainers for Postgres or `EFCore.InMemory` for unit tests) covering auth, smart playlists, marker detection, backup/restore. Add Vitest + React Testing Library for the SPA with at least smoke tests on `PlayerContext` and `App.tsx` routing.

---

## 3. Backend architecture

### 3.1 Layer boundaries

- **CRITICAL** `Vora.Application → Vora.Plugins` reference (see 2.10).
- **HIGH** `Vora.Domain → Pgvector.EntityFrameworkCore` (see 2.11).
- **LOW** `Vora.Application` references `Microsoft.AspNetCore.Http.Features 5.0.17` — `Vora.Application.csproj:11`. Five major versions behind the rest of the stack and Application is supposed to be HTTP-host-agnostic. Verify it's used at all; if not, delete.

### 3.2 Endpoint thinness

- **CRITICAL** `src/Vora.Api/Endpoints/MusicEndpoints.cs:720-752` — `StreamTrackAsync` builds a `ProcessStartInfo`, spawns `ffmpeg`, pumps stdout to the response body. Move into an `IAudioTranscodeService`.
- **HIGH** `src/Vora.Api/Endpoints/StreamingEndpoints.cs:103-140` — `PlaySessionAsync` orchestrates transcoding inline, taking a `StreamSession` Domain entity directly. Move into `IStreamManager.GetOrStartPlaybackAsync(...)` returning a `StreamPlaybackVM`.
- **HIGH** `src/Vora.Api/Endpoints/OverlayTemplateEndpoints.cs:24-52` — endpoint injects `IOverlayTemplateRepository` and constructs `OverlayTemplate` (Domain entity) inline. Add `IOverlayTemplateManager`.
- **HIGH** `src/Vora.Api/Endpoints/RecommendationEndpoints.cs:112-124` — `GetAiStatsAsync` injects `IOpenAiRecommendationRepository` directly with pagination inline.
- **MEDIUM** `src/Vora.Api/Endpoints/SettingsEndpoints.cs:77-93` — `GetHardwareDevices` probes `/dev/dri` in the endpoint. Wrap in `IHardwareCapabilityService`.
- **MEDIUM** `src/Vora.Api/Endpoints/ArtworkEndpoints.cs:26-44` — `ServeCustomArtwork` reads `IConfiguration`, resolves paths, sniffs MIME — all inline.

### 3.3 Manager / Service consistency

- **MEDIUM** Manager god classes. `src/Vora.Application/Media/MusicManager.cs` (883 lines, 9 ctor deps including `IConfiguration` and three `IEnumerable<I*Provider>`) mixes artwork upload, search, ratings, likes, Last.fm OAuth, playback, lyrics, and admin history. `src/Vora.Application/Media/MusicRecommendationManager.cs` (1321 lines) mixes daily-mix generation, radio queueing, station CRUD, year recap analytics, similar-artist lookup, and weekly mix scheduling, plus has 5 inline VM classes at lines 39-112 that belong in `Application/Media/ViewModels`.
- **MEDIUM** `src/Vora.Application/Media/MusicRecommendationManager.cs:226, 327` — `private static bool profileHasFewPlaysAsync(...)` is synchronous, lowercase first letter, has `Async` suffix. Three rule violations in one method.
- **LOW** `src/Vora.Application/Media/MediaManager.cs:49-69` — 9 ctor deps + `IConfiguration`. Same shape as MusicManager.

### 3.4 DI registration

- **POSITIVE** `src/Vora.Api/Program.cs` is clean — 14 lines, everything funneled through `ServiceRegistrationExtensions`.
- **MEDIUM** `src/Vora.Api/Extensions/PluginLoaderExtensions.cs:102-114` — `RegisterPluginType` iterates `PluginProviderInterfaces` and `break`s on the first match. A plugin implementing both e.g. `IMetadataProvider` AND `IArtworkProvider` only gets registered for the first one. Drop the `break;`.
- **LOW** `src/Vora.Api/Extensions/PluginLoaderExtensions.cs:53, 64, 92, 97` — plugin loader uses `Console.WriteLine` instead of `ILogger`. Plugin-load failures bypass the file/in-memory sink and won't appear in the admin Logs page.

### 3.5 Claims access discipline

- **MEDIUM** Casing mismatch between issuance and policy. `src/Vora.Application/Auth/AuthManager.cs:230` issues `"isAdmin"`; `ServiceRegistrationExtensions.cs:499` policy uses `RequireClaim("IsAdmin", "True")`. Works by accident — claim type matching is case-insensitive — but one refactor of `bool.ToString()` formatting silently locks admins out.
- **LOW** `src/Vora.Api/Endpoints/SmartListEndpoints.cs:65` — `var isAdmin = user.FindFirst("isAdmin")?.Value == "True";` — bypasses `AuthExtensions.IsAdmin()` (the only direct claim lookup in `Vora.Api`).

### 3.6 HttpClient discipline

- **MEDIUM** `src/Vora.Application/Collections/CollectionArtworkService.cs:73` — `using (var httpClient = new HttpClient())` per request, fetching arbitrary user-supplied URLs. Bypasses `IHttpClientFactory` and configured policies. (Same line is the SSRF surface — see 6.1.4.)

### 3.7 Plugin contracts

- **HIGH** `src/Vora.Plugins/Interfaces/IMediaIngestionService.cs` — `EnsureMovieAsync(Guid libraryId, ...) → Task<Guid>` etc. Plugins receive and return DB-row GUIDs; the boundary leaks Vora's persistence model.
- **MEDIUM** Cancellation token coverage is inconsistent across plugin interfaces. `ILibrarySyncProvider`, `IMusicArtworkProvider`, `IListeningDataProvider`, `ILyricsProvider`, `IPodcastDiscoveryProvider`, `IRequestServerLookup` take `CancellationToken`. `IMetadataProvider`, `IRatingsProvider`, `IRequestProvider`, `IDiscoveryProvider`, `IRecommendationProvider`, `IArtworkProvider`, `ICalendarProvider`, `IFolderWatcherProvider`, `IOverlayProvider`, `IChronologyProvider` don't. Network-bound provider calls cannot be cancelled when the originating HTTP request aborts.
- **MEDIUM** Provider-specific parameter names. `IMetadataProvider.FetchEpisodeMetadataAsync(string showTmdbId, ...)` (line 13), `IArtworkProvider.GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, ...)` (line 8) hardcode provider vocabulary into the interface. Use an `ExternalIdSet` or `Dictionary<string,string>`.
- **LOW** `IVoraPlugin.SupportedLibraryTypes` is `string[]`. Casing mismatch is invisible until runtime.
- **LOW** No plugin version compatibility surface. `IVoraPlugin.Version` is free-form. Add `int ContractVersion { get; }` and refuse to load mismatched plugins.

### 3.8 Concurrency / async patterns

- **CRITICAL** Fire-and-forget Task.Run in scoped `IptvManager` (see 2.13).
- **HIGH** `MusicEndpoints.RefreshRecommendationsAsync` fires Task.Run on scoped manager (see 2.13).
- **MEDIUM** `src/Vora.Application/Iptv/DvrRecordingService.cs:75, 114` — fire-and-forget without cancellation propagation; discarded `process.StandardError.ReadToEndAsync()` task with `CancellationToken.None`.
- **MEDIUM** `src/Vora.Infrastructure/FileSystem/FolderWatcherService.cs:28-63` — sync `void StartWatching(...)` does `_ = Task.Run(async () => ...)`. If the inner task throws before adding to `_activeWatchers`, no caller will know. Make async.
- **MEDIUM** `src/Vora.Infrastructure/Transcoding/FFmpegTranscodeService.cs:100` — `await Task.Delay(3000)` without cancellation. Method signature lacks `CancellationToken` (contract gap on `ITranscodeService`).
- **MEDIUM** `src/Vora.Application/Tasks/DvrWorker.cs:30, 39` — `ProcessRecordingsAsync()` doesn't accept `stoppingToken`. Inner work runs unbounded once started.
- **MEDIUM** `src/Vora.Infrastructure/Analysis/FFmpegAnalyzerService.cs:62` — `await process.WaitForExitAsync();` with no CT and no timeout. A hung FFmpeg pins a worker thread forever.
- **LOW** `src/Vora.Application/Metadata/MetadataFetchService.cs:65, 191, 218, 233` — `Task.Result` after `WhenAll`. Not a deadlock but stylistic — prefer `await taskA` after `WhenAll`.
- **NIT** `src/Vora.Application/Auth/AuthManager.cs:269` uses `Encoding.ASCII.GetBytes(secret)`; `ServiceRegistrationExtensions.cs:493` uses `Encoding.UTF8.GetBytes(jwtSecret)`. A non-ASCII secret byte makes tokens unverifiable.

### 3.9 Real-time hub

- **CRITICAL** `VoraHub` mapped without `RequireAuthorization()` (see 2.8).
- **HIGH** Per-profile events broadcast to all clients. `src/Vora.Api/Hubs/SignalRClientNotifier.cs:14-80` — all 18 notification methods use `Clients.All`. Per-profile mix updates fan out to every connected client; access/permission events disclose which user IDs have been modified.
- **HIGH** Hub trusts JWT `isAdmin` claim at connection time without re-validation. `src/Vora.Api/Hubs/VoraHub.cs:8-15` — `OnConnectedAsync` reads `Context.User?.IsAdmin()` straight off the JWT to join `admins` group. A user demoted from admin keeps receiving admin alerts for the life of their token.
- **MEDIUM** `IClientNotifier` lives in `src/Vora.Application/Analysis/IClientNotifier.cs:6` — cross-cutting interface in an analysis-specific namespace. Move to `Vora.Application.Notifications` or `Vora.Application.Realtime`.

### 3.10 Workers / background jobs

- **MEDIUM** Workers live in both `Vora.Application` and `Vora.Infrastructure`. CLAUDE.md and `docs/architecture.md` say workers live in `Vora.Infrastructure/Workers/` — reality: only 2 of 11 are there. `Vora.Application/Tasks/{DvrWorker, DvrPostProcessingWorker, DvrStorageMonitorWorker, ScheduledJobWorker, TaskProcessingWorker}.cs`, plus `TimeshiftJanitorWorker`, `PodcastFeedRefreshWorker`, `RecommendationRefreshWorker`, `BackupScheduleWorker` are all in Application but pull in `Microsoft.Extensions.Hosting` and spawn processes (e.g. `DvrPostProcessingWorker.cs:146`). Move them to Infrastructure (or update docs).
- **LOW** `src/Vora.Application/Media/RecommendationRefreshWorker.cs:95, 120-134` — uses local time for schedule comparison. Inside a container without `TZ` set, "Daily3am" runs at 3am UTC. Surprising.

### 3.11 Configuration & secrets

- **HIGH** No typed options pattern. `IConfiguration` is reached for directly in 16+ places across Application and even in `Infrastructure/Persistence/Repositories/LibraryRepository.cs:228`. The string key `StoragePaths:CustomArtwork` is duplicated in 8+ files with slightly different fallback paths.
  - Affected files include `MediaManager.cs:255`, `MusicManager.cs:101`, `MediaIngestionService.cs:41`, `MetadataMappingService.cs:544`, `ArtworkService.cs:38`, `CollectionArtworkService.cs:29`, `PosterOverlayManager.cs:40, 44`, `UserProfileImageService.cs:24`, `VideoThumbnailStorageService.cs:15`, `IptvEpgService.cs:62`, `IptvPassthroughService.cs:471`, `DvrRecordingService.cs:65`, `PluginManager.cs:97`, `AuthManager.cs:267`.
  - **Fix:** Introduce `StoragePathsOptions { CustomArtwork, UserImages, VideoThumbnails, Plugins, EpgCache, IptvDvr, OriginalArtworkCache, Logs, Backups, DataProtection }` + `JwtOptions` + `FileSystemBrowserOptions`. `services.AddOptions<...>().Bind(...).ValidateDataAnnotations().ValidateOnStart();`
- **HIGH** `src/Vora.Application/Iptv/IptvPassthroughService.cs:471` reuses the JWT secret for IPTV HMAC AND silently defaults to `string.Empty`. Two security domains coupled, plus the empty default.
- **HIGH** `src/Vora.Infrastructure/Persistence/Repositories/LibraryRepository.cs:228-240` — `SweepPhysicalOverlays` reads storage config from `IConfiguration` and deletes files from disk inside a repository class. Belongs in an application/domain service.

---

## 4. Data architecture

### 4.1 Indexes

- **HIGH** No HNSW/IVFFlat index on `MediaItemEmbedding.Embedding`. `VoraDbContext.cs:1272-1284`, `Initial.cs:1525`. `OpenAiRecommendationRepository.VectorSearchUnwatchedMediaAsync` (`OpenAiRecommendationRepository.cs:43-51`) does `ORDER BY CosineDistance` over the whole table. **Add a migration:** `CREATE INDEX ix_media_item_embeddings_cosine ON "MediaItemEmbeddings" USING hnsw ("Embedding" vector_cosine_ops);`
- **HIGH** No indexes on `MediaItem.TmdbId`, `MediaItem.ImdbId`, `MediaItem.TvdbId`. `VoraDbContext.cs:221-223`. Queried in `MediaRepository.MediaExistsByExternalIdAsync` / `GetMediaIdsByExternalIdsAsync` / `GetLibraryIdsByTmdbIdsAsync`. Add at minimum `(TmdbId) WHERE TmdbId IS NOT NULL` and ditto ImdbId.
- **HIGH** `AdminNotifications.CreatedAt` not indexed. `AdminNotificationRepository.GetRecentAsync` (line 26) does `OrderByDescending(n => n.CreatedAt).Take(limit)`. Add `HasIndex(e => new { e.IsRead, e.CreatedAt })`.
- **HIGH** `IptvRecordingSession` lacks indexes on `StartTime`/`EndTime`/`Status`. `VoraDbContext.cs:983-995`. The DVR worker scans by time + status; currently full-table.
- **MEDIUM** `StreamSession` — no index for active-session lookup (`EndedAt IS NULL`). Consider a partial index `WHERE "EndedAt" IS NULL` on `(UserId, StartedAt)`.
- **MEDIUM** `SystemMetric` has no configuration at all and no index on `Timestamp`. Time-series tables always get queried by time range.
- **MEDIUM** `AiUsageLogs.Timestamp` unindexed. Only `ProfileId` is indexed.
- **MEDIUM** `EmailDeliveryLog` missing `(Status, CreatedAt)` for the retry worker's "pending failures" path.
- **MEDIUM** `IptvChannels.ExternalChannelId` is indexed non-uniquely but should be `(PlaylistId, ExternalChannelId) UNIQUE` to prevent duplicates within a playlist during EPG sync.
- **LOW** `TrackPlayHistory` has both `(ProfileId, TrackId)` and `TrackId` alone — redundant.

### 4.2 Connection pooling + DbContext lifetime

- **HIGH** `ServiceRegistrationExtensions.cs:206-210` uses `AddDbContext` not `AddDbContextPool`. For a server with SignalR + frequent IPTV/streaming pings, pooling will noticeably reduce GC pressure. The context has no per-instance state that conflicts with pooling.
- **LOW** No Npgsql retry-on-failure (`npgsql.EnableRetryOnFailure()` not enabled). Fine if Postgres is co-located; helpful if it ever moves.

### 4.3 Query patterns / N+1 risk

- **HIGH** `MediaRepository.GetForMetadataSyncAsync` (`MediaRepository.cs:156-185`) — chains `LoadAsync` per episode (lines 174-181) for `Season → TvShow → Cast`. N+1 over episode count. Pre-load via composed `.Include().ThenInclude()` instead.
- **HIGH** `UserMediaStateRepository.GetContinueWatchingAsync` (`UserMediaStateRepository.cs:258-262`) — correlated subquery per episode row for `ResumePositionSeconds`. EF inlines as LATERAL joins but multiplies work. Pre-batch.
- **HIGH** `MediaRepository.GetMarkerCoverageAsync` (`MediaRepository.cs:285-322`) — 7 round-trip `CountAsync` calls on the same query. Collapse to one query with conditional sums.
- **HIGH** `SmartPlaylistEvaluator.BuildMusicRowQuery` (`SmartPlaylistEvaluator.cs:134-138`) — correlated `PlayCount` + `LastPlayedAt` + `Liked` subqueries per track row. Pre-aggregate.
- **HIGH** `SmartPlaylistEvaluator.BuildMovieRowQuery` / `BuildEpisodeRowQuery` (lines 175, 227) — `m.MediaParts.First().Duration` projection translates to subquery per row. Use `MediaItemAnalysis.Duration` instead.
- **MEDIUM** `MusicRepository.GetAdminPlayHistoryAsync` (`MusicRepository.cs:447-466`) — `.ToLower().Contains(...)` defeats any text index. Use `EF.Functions.ILike`.
- **MEDIUM** General pattern: `.ToLower().Contains(searchLower)` everywhere (`MusicRepository.cs:343, 361, 381, 460-464, 590, 602`; `SearchRepository.cs:39, 51, 69`). Replace with `EF.Functions.ILike(field, $"%{value}%")` and add `pg_trgm` indexes on the searched columns. **Single biggest pattern-level win.**
- **MEDIUM** `MusicRepository.UpdateTrackAsync` / `UpdateAlbumAsync` / `UpdateArtistAsync` (lines 42-51, 181-184) — `_context.X.Update(entity)` marks every property modified.
- **MEDIUM** `MusicRecommendationRepository.GetTopArtistsForProfileAsync` (`MusicRecommendationRepository.cs:93-106`) — `Math.Exp` forces in-memory grouping. Compute days-since in SQL, exp in memory only on aggregated groups.
- **MEDIUM** `MusicRecommendationRepository.GetYearsWithHistoryAsync` (lines 443-449) — pulls all `PlayedAt`, distincts in memory. Use `Select(... .Year).Distinct()` server-side.
- **MEDIUM** `MusicRepository.GetGenreSummariesAsync` (lines 412-414, 427-437) — materializes all albums then groups in memory.
- **MEDIUM** `MusicRecommendationRepository.GetTopTracksByArtistAsync` (line 144) — materializes every track for an artist before counting plays.
- **MEDIUM** `MusicRecommendationRepository.GetTopTracksByGenresAsync` / `GetTracksByGenreAsync` (lines 320, 348) — `OrderBy(_ => Guid.NewGuid())` after materialization. Use `EF.Functions.Random()` server-side.
- **LOW** `MediaRepository.GetMediaIdsMissingMetadataAsync` (line 189) — returns IDs only but tracks entities. Add `AsNoTracking()`.
- **LOW** Most read paths could use `.AsNoTracking()`. Spot-check the read repositories.

### 4.4 Transactions

- **MEDIUM** Only one explicit transaction in the entire codebase (`MediaRepository.ReplaceMarkersAsync` — `MediaRepository.cs:222-253`). Multi-step write operations like `MusicRecommendationRepository.ReplaceSimilaritiesAsync` (lines 460-467), `ReplaceArtistTagsAsync` (lines 478-485), `UserMediaStateRepository.SetMediaPlayedStateAsync` (lines 417-464) do `Remove`/`Add` pairs without a transaction. Wrap them.
- **MEDIUM** `UserMediaStateRepository.SetMediaPlayedStateAsync` (lines 438-463) — `ExecuteUpdateAsync` then `SaveChangesAsync` are not atomic. Wrap.

### 4.5 DbContext organization

- **NIT** `VoraDbContext.cs` is 1500 lines, no `IEntityTypeConfiguration<T>` classes. At this size moving each aggregate into a `Persistence/Configurations/` file via `modelBuilder.ApplyConfigurationsFromAssembly(...)` would significantly reduce merge friction.
- **NIT** `ConfigureNotifications` (lines 1235-1255) uses a hand-rolled `ValueComparer<List<WebhookEventType>>` while every other list converter goes through `ListValueConverters`. Generify or absorb the webhook converter.
- **NIT** `SeedReferenceData` and `SeedSystemDefaults` (lines 1353-1469) bake static seeds (27 TMDB genre rows, 6 SmartList rows) into `OnModelCreating`. Every edit churns a migration. Move to idempotent runtime seed.

### 4.6 JSON columns / owned types

- **MEDIUM** Multiple `List<string>` / `List<Guid>` columns serialized as JSON in text (`ListValueConverters`, `VoraDbContext.cs:1471-1505`). Used on `User.AllowedLibraryIds`, `User.AllowedIptvPlaylistIds`, `UserProfile.AllowedLibraryIds` + others, `MediaLibrary.FolderPaths`, `MediaItem.LockedFields`, etc. Postgres has native `text[]` / `uuid[]` with GIN indexes — significantly better for `Contains` queries.
- **MEDIUM** `SmartPlaylist.RulesJson`, `SmartList.FilterRulesJson`, `OverlayTemplate.ConfigurationJson`, `ServerSetting.BackupConfigurationJson`, `IptvRecordingSession.CommercialMarkersJson` — switch from `text` to `jsonb` (`HasColumnType("jsonb")`) for GIN indexability.
- **MEDIUM** `WebhookConfig.SubscribedEvents` serialized via hand-rolled JSON converter. Use a Postgres enum array.

### 4.7 Composite keys / unique constraints

- **MEDIUM** `UserMediaState` has both `Id` PK and `(ProfileId, MediaItemId)` unique index. The composite is the natural key. Same pattern in `UserMediaRating`, `UserAlbumRating`, `UserArtistRating`, `TrackLike`, `PodcastSubscription`, `PodcastEpisodeProfileState`, `ProfileDeviceSetting`, `UserWatchlistItem`. Surrogate `Id` is unused; drop or promote composite to PK.
- **MEDIUM** `MediaCastMember` keyed on `(ActorId, MediaItemId)` only — prevents an actor from holding multiple roles in the same media. Include `Roles` flag or role id.

### 4.8 Concurrency tokens

- **MEDIUM** Zero entities use `[ConcurrencyCheck]` / `RowVersion` / `IsConcurrencyToken`. Especially relevant for `ServerSetting` (single row, edited concurrently), `UserProfile`, `MediaLibrary`, `IptvPlaylist`. Consider `entity.UseXminAsConcurrencyToken()` on `ServerSetting`.

### 4.9 Migrations

- **LOW** Migration history is fresh (May 21-23, 2026), 12 user migrations, none edit prior migrations. Clean.
- **LOW** `20260523022704_MarkersAndAutoSkipPreferences.cs:14-24` drops three columns from `MediaItemAnalysis` (`CreditsStart`, `IntroEnd`, `IntroStart`) added on day 0. Pre-release churn — fine, but if there's any existing deployment, those values were lost.
- **NIT** `20260521054433_AddDefaultSpotlightList.cs` is a data-only migration that flips one bool on one row. Move this kind of thing to idempotent runtime seeding (`EnsureSeedAsync` at startup).

### 4.10 DataProtection / pgvector

- **POSITIVE** DataProtection setup is correct (`ServiceRegistrationExtensions.cs:439-459`) — reads `StoragePaths:DataProtection`, persists to disk, application name "Vora" set. Deployment must mount as volume per CLAUDE.md.
- **NIT** DataProtection keys are not encrypted at rest (no `ProtectKeysWithCertificate`). Acceptable for self-hosted single-server; flag for multi-tenant futures.
- **NIT** `MediaItemEmbedding.Embedding` is nullable but rows are created post-population. Either make `IsRequired()` or use the null sentinel intentionally.

### 4.11 Soft delete / audit

- **MEDIUM** No project-wide soft-delete convention. Most entities lack `UpdatedAt`. If admin audit is ever desired, adding `UpdatedAt` consistently via a base class + SaveChanges interceptor is cheap.

---

## 5. Frontend code quality

### 5.1 What's clean (good news)

- **No `: any` / `as any` / `<any>` / `@ts-ignore` anywhere.** Strict-typing rule is fully observed; all `: unknown` hits are `catch (err: unknown)` patterns.
- **No `alert()` / `confirm()` / `prompt()` calls.** `useDialog` is used everywhere — but see 5.7 for hand-rolled re-implementations.
- **No direct `axios.get/post` outside `api/client.ts` and the service layer.** Only `isAxiosError` (a type narrower) is imported elsewhere. Clean.
- **No `is_admin` localStorage key anywhere.** `is_server_admin` is canonical. Clean.

### 5.2 Color tokens — heavily violated

589 hardcoded Tailwind palette uses across 39 files. Worst offenders are all foundational primitives, so the brand-recolor cascade doesn't work end-to-end.

- **HIGH** `src/Vora.Web/src/components/Common/Modal.tsx:11, 37-41` — the shared `Modal` primitive hardcodes Tailwind palette in its dark surface variants (`bg-gray-950`, `border-gray-800`, etc.). Every client-side modal uses this primitive, so the entire modal layer can't be retemplated. Replace with `var(--vora-bg-raised)` / `var(--vora-border-subtle)`.
- **HIGH** `src/Vora.Web/src/dialogs/Dialog.tsx:112-153` — the global dialog primitive uses palette (`bg-gray-900`, `text-white`, `focus:border-orange-500`, etc.). Every alert/confirm/prompt renders with hardcoded brand colors.
- **HIGH** `src/Vora.Web/src/components/Media/MediaCard.tsx:31-95` — the heavily-used poster primitive uses palette throughout. Drives ~13 violations and ships on every poster on Home/Library/Search/Watchlist.
- **HIGH** `pages/Client/Playlists/SmartPlaylistEditorModal.tsx:251-310+` — 34 palette uses in one file.
- **HIGH** `components/Media/EditMetadataModal.tsx` (25 uses) + `components/Media/MusicMetadataEditModal.tsx` (22 uses) — admin/metadata modals bypass tokens.
- **MEDIUM** `src/Vora.Web/src/contexts/PlayerContext.tsx:732-748` — the "Stream Terminated" overlay uses palette.
- **MEDIUM** `src/Vora.Web/src/layouts/MainLayout.tsx:342-348` — restricted-by-schedule splash screen uses palette. First thing a time-restricted profile sees.
- **MEDIUM** `src/Vora.Web/src/components/Player/GlobalVideoPlayer.tsx:23-29` — `chipStyle` uses literal `#fafafa` text and `rgba(20, 20, 28, 0.72)` background — not tokens. Many inline `style={{ background: 'rgba(...)' }}` color literals.

Full file ranking (palette hits per file): `SmartPlaylistEditorModal 34 / EditMetadataModal 25 / MusicMetadataEditModal 22 / DvrSessionCard 16 / HomeCustomizeModal 14 / MediaCard 13 / CreateCollectionModal 12+ / DiscoveryDetailsPage 12+ / EditCollectionModal 12+ / DiscoveryCustomizeModal 10`.

### 5.3 Modal z-index

Header is `z-[100]`. Several overlays sit below.

- **HIGH** `src/Vora.Web/src/pages/Profile/AccountSettingsPage.tsx:199` — modal overlay at `z-50`.
- **HIGH** `src/Vora.Web/src/pages/Admin/Libraries/ManageLibrary.tsx:583, 595` — hand-rolled alert/confirm modals at `z-50`. Should be `useDialog`.
- **HIGH** `src/Vora.Web/src/pages/Admin/SmartLists/SmartListsPage.tsx:242` — create/edit modal at `z-50`.
- **MEDIUM** `src/Vora.Web/src/components/Client/Primitives/QualityPanel.tsx:71` — slide-out panel at `z-[60]`. Below header.
- **MEDIUM** `src/Vora.Web/src/components/Common/Modal.tsx:4` — the `ModalZIndex` type permits `z-50`/`z-[60]`/`z-[100]`. Remove those from the union to enforce convention at compile time.
- **MEDIUM** `src/Vora.Web/src/components/Admin/Features/FeaturePluginList.tsx:97` — alert modal at `z-[9999]`. Overshoots; competes with the global video player.

### 5.4 PlayerContext re-render / hook patterns

- **CRITICAL** Provider value rebuilt every render with no `useMemo` (see 2.15).
- **HIGH** Inconsistent `useCallback` in `PlayerContext.tsx`. `playMedia`, `closePlayer`, `nextTrack`, `previousTrack`, `applyEqPreset`, `ensureAudioGraph` are wrapped — but `togglePlayPause` (line 686), `seek` (692), `skipForward` (699), `skipBackward` (703), `setVolume` (707), `changeStreams` (646), and the inline `toggleFullscreen` (722) are not. They get new identity every render and amplify the rerender problem.
- **HIGH** Video listener effect depends on full `currentMedia` object (lines 289-361). `useEffect(..., [currentMedia, playMedia])` rebinds 6 listeners on any field change. Should depend on `videoRef.current` (stable) and an id.

### 5.5 Routing / app shell

- **HIGH** `src/Vora.Web/src/App.tsx` — no `React.lazy` / code-splitting on any route. All ~50 page components statically imported. Auth pages, admin pages, and 2040-line MusicTab all ship in the login-page bundle.
- **HIGH** `src/Vora.Web/src/App.tsx:212-279` — admin routes lack admin guard. `RequireAuth` only checks profile token exists. A non-admin profile can navigate to `/admin/users`; the shell renders, API 403s, user sees a broken page instead of a redirect. Add `RequireAdmin`.
- **HIGH** `src/Vora.Web/src/App.tsx:124-127` — `RequireAuth` mutates state during render (calls `serverVault.setActiveServerId` in render body). Move into a `useEffect` and return loader on first pass.
- **MEDIUM** `src/Vora.Web/src/App.tsx:212-279` — admin route block duplicated 1:1 between `/admin` and `/server/:serverId/admin`. ~30 child routes copy-pasted. Drift risk is real — `discovery`/`for-you` indentation is already inconsistent between the two blocks (lines 261-266 vs 226-232).

### 5.6 SignalR usage

- **HIGH** `src/Vora.Web/src/hooks/useSignalREvent.ts:8-43` — `callback` in the effect deps re-binds the listener every render. Almost no caller wraps the callback in `useCallback`. Use a ref-based pattern.
- **HIGH** `useSignalREvent.ts:5-30` — module-level singleton connection with no ref counting. On URL change, calls `sharedConnection.stop()` (non-awaited) then `.start()`. Multiple components mounting at once race.
- **MEDIUM** Event names are stringly-typed. Add `export const VORA_EVENTS = { ... } as const` registry and type the hook generically.
- **LOW** `useSignalREvent.ts` swallows start errors via `.catch(err => console.error(...))`.

### 5.7 Hand-rolled dialog re-implementations

- **MEDIUM** `pages/Admin/Libraries/ManageLibrary.tsx:582-611` re-implements alert/confirm modals inline. Duplicates dialog logic, gets z-index wrong (see 5.3), uses palette.
- **MEDIUM** `pages/Admin/SmartLists/SmartListsPage.tsx:241` and `components/Admin/Features/FeaturePluginList.tsx:96` — same pattern. Centralize to `useDialog`.

### 5.8 localStorage key inventory

Distinct keys observed: `device_id`, `profile_token`, `account_token`, `user_id`, `profile_name`, `is_server_admin`, `is_profile_admin`, `auto_login_profile_id`, plus dynamic `vora_show_spotlight_${profileId}`, `iptv_prefs_${profileId}_${deviceId}`, `music_nav_state`, `music_nav_profile`. Plus sessionStorage: `pending_server_url`, `pending_user_token`.

- **HIGH** Keys scattered across 30+ files as bare string literals. Renames require grep-and-replace across the codebase. Centralize in `src/utils/storageKeys.ts`:
  ```ts
  export const StorageKeys = {
    deviceId: 'device_id',
    profileToken: 'profile_token',
    accountToken: 'account_token',
    userId: 'user_id',
    profileName: 'profile_name',
    isServerAdmin: 'is_server_admin',
    isProfileAdmin: 'is_profile_admin',
    autoLoginProfileId: 'auto_login_profile_id',
    spotlight: (profileId: string) => `vora_show_spotlight_${profileId}`,
    iptvPrefs: (profileId: string, deviceId: string) => `iptv_prefs_${profileId}_${deviceId}`,
    musicNavState: 'music_nav_state',
    musicNavProfile: 'music_nav_profile',
  } as const;
  ```
  Also expose a typed JWT parser helper — the `JSON.parse(atob(token.split('.')[1])).sub` pattern is duplicated at `LiveTvGuide.tsx:40, 95`, `MusicTab.tsx:34`, `App.tsx:84` (≥6 sites).
- **MEDIUM** `iptv_prefs_${profileId}_${deviceId}` and `music_nav_state` invented without doc — not in `docs/auth-and-devices.md`.

### 5.9 Component sizes (refactoring candidates)

- **HIGH** `src/Vora.Web/src/pages/Client/Audio/MusicTab.tsx` (2040 lines) — god-page with 30+ `useState`, 9 views inline. Split via nested routes.
- **HIGH** `src/Vora.Web/src/pages/Client/SettingsPage.tsx` (963 lines) — multiple tabs inline. Split into `pages/Client/Settings/<tab>.tsx`.
- **HIGH** `src/Vora.Web/src/pages/Client/LiveTv/LiveTvGuide.tsx` (928 lines) — channel/category + manual virtualizer + render loop in one file. Extract `useGuideData`, `useGuideVirtualization`, `<GuideRow>`.
- **MEDIUM** `PodcastsTab.tsx` (808), `PlayerContext.tsx` (754), `BackupsPage.tsx` (732), `AccountSettingsPage.tsx` (726), `MusicMetadataEditModal.tsx` (713), `musicService.ts` (701), `LiveTvPlayer.tsx` (699), `IptvPage.tsx` (674), `GlobalVideoPlayer.tsx` (673) — all candidates. The two video players in particular could share a `BasePlayer` (~300 lines saved).

### 5.10 API service consistency

- **CRITICAL** Axios instance rebuilt every call (see 2.16).
- **HIGH** No request cancellation support. `VoraRequestConfig` extends `AxiosRequestConfig` so `signal` could be passed, but no service does. Every page navigating mid-fetch logs stale-response rejections.
- **MEDIUM** Global response interceptor only logs 401, doesn't act (`client.ts:68-76`). No auto-redirect to `/login`, no token refresh, no event dispatch.
- **MEDIUM** Repeated unsafe error narrowing pattern `(err as { response?: { status?: number } })?.response?.status` at `musicService.ts:524, 590, 661` etc. Extract `getResponseStatus(err: unknown)` helper using `isAxiosError`.

### 5.11 Accessibility

- **HIGH** Player progress bars are not keyboard-accessible. `GlobalVideoPlayer.tsx:497-510, 615-628`, `LiveRadioPlayer.tsx`, `LiveTvPlayer.tsx`, `NowPlayingFullscreen.tsx` — `<div onClick onMouseMove>` with no `role="slider"`, no `tabIndex`, no `aria-valuenow/min/max`, no `onKeyDown`. Global grep for `role="slider"` in `components/Player/**` returns zero.
- **MEDIUM** `dialogs/Dialog.tsx:104-108` is correct, but `components/Common/Modal.tsx` has no `role="dialog"`, no `aria-modal="true"`, no `aria-labelledby`. Every modal using it is invisible to screen readers.
- **MEDIUM** No focus trap inside Modal. `Modal.tsx:43-85` handles Escape but doesn't manage focus.

### 5.12 Performance smells

- **HIGH** `LiveTvGuide.tsx:698-712` — expensive per-row program-normalize runs every render inside `.map`. The clean+sort loop recomputes for every virtual row on every scroll-tick. Precompute once via `useMemo` keyed on `guideData`.
- **HIGH** `setCurrentTime` 4×/sec on un-memoized context value (see 2.15).
- **MEDIUM** `LiveTvGuide.tsx:719-790` — inline lambdas per row in virtualized list.
- **MEDIUM** No lazy loading on `App.tsx` routes (see 5.5).

### 5.13 Dead frontend code (also listed in section 7)

- `components/Admin/Primitives/Section.tsx` — 0 importers.
- `components/Client/Primitives/Glass.tsx` — 0 importers (referenced in `docs/redesign/design-language.md` as planned).
- `components/Client/Primitives/Chip.tsx` — 0 importers.
- `components/Client/Shell/clientNavData.tsx` — 61 lines, exports `CLIENT_NAV`, `ClientIcons`, `resolveClientPath`, `ClientNavEntry`, `ClientIconName`; none imported anywhere (sibling `adminNavData.tsx` IS used).
- `components/YouTube/YouTubeChannelBadge.tsx` — 0 importers.

---

## 6. Cross-cutting concerns

### 6.1 Security

- **CRITICAL** Items 2.1-2.9 above (JWT, hub, DVR/HLS auth, path traversal, ownership).
- **HIGH** SSRF via "Add artwork by URL" — `src/Vora.Application/Artwork/ArtworkService.cs:87-108`. `AddUrlAsync` fetches any user-supplied URL with no scheme/host allowlist, no DNS validation against RFC1918/loopback/link-local, no response-size cap, no content-type validation. Any authenticated profile can hit cloud metadata endpoints.
- **HIGH** No rate limiting on login / register / password-reset confirm. `AuthEndpoints.cs:47, 51, 101, 135` + `AuthManager.cs:194, 394`. `RequestPasswordResetAsync` throttles (3/hour/email) but `ConfirmPasswordResetAsync` doesn't, and the server has no `AddRateLimiter` registration at all.
- **HIGH** `X-Forwarded-For` trusted without configuration. `DeviceTrackingMiddleware.cs:182` reads the header as-is to log client IP. No `UseForwardedHeaders` with `KnownProxies`. Clients spoof their IP.
- **HIGH** Geo lookup over plain HTTP. `DeviceTrackingMiddleware.cs:209` — `GetStringAsync("http://ip-api.com/json/{ip}")`. MITM can return arbitrary location strings that get persisted and rendered to admin UI.
- **HIGH** No security headers. `WebApplicationExtensions.cs:9-29` has `UseHttpsRedirection` but no `UseHsts`, no header middleware. Responses lack HSTS, CSP, X-Frame-Options, X-Content-Type-Options.
- **HIGH** AdminOnly policy fragile via claim casing mismatch (see 3.5).
- **MEDIUM** CORS hard-coded to `localhost:5173`. `ServiceRegistrationExtensions.cs:505-517`. No config-driven additional-origins list — users will copy-paste for prod.
- **MEDIUM** Weak password policy. `AuthManager.cs:52, 401` — `MinPasswordLength = 6`, enforced only on reset. Register accepts any non-empty string.
- **MEDIUM** Filesystem browser exposes whole drive root on Windows by default. `FileSystemBrowserService.cs:144-166` — without `FileSystemBrowser:AllowedRoots`, every Windows drive root or `/` on Linux is walkable by admins.
- **MEDIUM** Stream session "command" broadcast to all clients. `StreamingAdminEndpoints.cs:49-61` — `SendSessionCommandAsync` uses `Clients.All` rather than scoping to the device/profile.
- **MEDIUM** FFmpeg shell-arg injection via string interpolation. Files: `DvrRecordingService.cs:96`, `FFmpegAnalyzerService.cs:40, 119, 288`, `FFmpegVideoThumbnailGeneratorService.cs:23, 80`. Not shell injection (`UseShellExecute=false`) but malicious filenames containing `"` break argument parsing. Use the safer `psi.ArgumentList.Add(...)` pattern that `MusicEndpoints.cs:732-739` already uses.
- **LOW** Password reset throttling is per-process `IMemoryCache`. Resets on container restart.

### 6.2 Observability

- **HIGH** No `/health` endpoint. Glob shows zero `MapHealthChecks` / `AddHealthChecks`. Docker/Unraid/K8s have nothing to probe.
- **HIGH** No global exception handler. `WebApplicationExtensions.cs:9-29` — no `UseExceptionHandler`, no `UseStatusCodePages`. Endpoint try/catch is ad-hoc — some `Results.BadRequest(new { ex.Message })`, some `Results.Problem`, some `Results.Json(..., 403)`. Stack traces leak in dev env (which `docker-compose.yml` defaults to).
- **MEDIUM** No metrics / tracing. No OpenTelemetry, Prometheus, `System.Diagnostics.Activity`, or `Meter`. Stream-session, transcode-queue, EPG-sync timings invisible.
- **MEDIUM** `Console.WriteLine` left in plugin & theme loaders. Files: `PluginLoaderExtensions.cs:53, 64, 92, 97`; `IThemeBundleLoader.cs:70, 82, 86, 91, 146`; `IClientTemplateBundleLoader.cs:64, 76, 80, 85, 138`. Bypasses `VoraLoggerProvider` ring buffer + file sink.
- **LOW** Inconsistent error envelope shapes — frontend can't reliably extract a single `.message`/`.error`/`.title` field.

### 6.3 Performance

- **HIGH** Fire-and-forget Task.Run captures scoped services (see 2.13).
- **HIGH** Sync EF call in middleware. `DeviceTrackingMiddleware.cs:108` — `dbContext.ClientDevices.FirstOrDefault(...)` should be `FirstOrDefaultAsync`.
- **MEDIUM** No response compression. `AddResponseCompression` not registered.
- **MEDIUM** No `Cache-Control` on static artwork/thumbnail/VTT endpoints. VTT/sprite do set `EntityTag`/`LastModified` (good), but `Cache-Control: public, max-age=...` is missing for GUID-named artwork.
- **MEDIUM** `Clients.All` for high-frequency events (see 3.9 second bullet).
- **LOW** `DeviceLocks` `ConcurrentDictionary` grows unbounded. `DeviceTrackingMiddleware.cs:22` — `SemaphoreSlim`s never removed. Slow leak over years.

### 6.4 Resilience

- **HIGH** No HTTP retry / circuit breaker on external providers. `ServiceRegistrationExtensions.cs:362-434` — all `AddHttpClient` calls (TMDB, Last.fm, MusicBrainz, FanartTv, TheAudioDb, LrcLib, Genius, Plex, YouTube, ip-api, IPTV, podcasts) set `Timeout` only. Add `.AddStandardResilienceHandler()` from `Microsoft.Extensions.Http.Resilience`.
- **MEDIUM** Cancellation tokens not threaded through endpoints. Out of ~45 endpoint files, only 6 declare `CancellationToken`. Long discovery/search/library scans continue server-side after the client aborts.
- **MEDIUM** Endpoints catch `Exception` broadly and return `BadRequest`. E.g. `AdminEndpoints.cs:71`, `StreamingEndpoints.cs:85-88`. A DB outage becomes a 400 with internal exception message.

### 6.5 Build & deploy

- **CRITICAL** `docker-compose.yml:10` — `ASPNETCORE_ENVIRONMENT=Development` (see 2.14).
- **MEDIUM** `docker-compose.yml:26-28` — hardcodes `D:\TestServer\...` paths committed to the repo. Make `.env`-driven (`${MEDIA_MOVIES:-/srv/media/movies}`).
- **LOW** `src/Vora.Api/Dockerfile:36-40` — no `USER app` directive. Runs as root. The `aspnet` images ship a `app` user; switch.
- **LOW** `.dockerignore` excludes `**/Dockerfile*` and `**/docker-compose*` — intentional MS template, but verify your build flow.

### 6.6 Documentation

- **LOW** Docs not indexed by `CLAUDE.md`: `docs/admin-design-spec.md`, `admin-reorg-plan.md`, `admin-theme-bundles.md`, `multi-server-audio-plan.md`, `youtube-feature-spec.md`, `redesign/README.md`, `redesign/information-architecture.md`, `redesign/page-redesigns.md`, `redesign/rollout-plan.md`. Either link them or archive to `docs/archive/`. Note `admin-design-spec.md` is referenced from `src/Vora.Web/src/theme/types.ts:10` in a comment but appears to describe an already-done "Phase 1 hardcodes / Phase 2 tokens" plan.

---

## 7. Dead code & unused files

Substantiated by grep (0 references outside the definition file).

### 7.1 C# types — true orphans

1. `HardwareProfile` — `src/Vora.Domain/ValueObjects/HardwareProfile.cs`. **Delete.**
2. `InvitationConsumptionInfo` — `src/Vora.Application/Auth/IInvitationManager.cs:29`. **Delete.**
3. `ToggleAdminRequest` — `src/Vora.Api/Endpoints/SmartListEndpoints.cs:11`. DTO with `ShowToAdminOnly`; 0 references. **Delete.**

### 7.2 C# interface methods (declared + implemented, 0 callers)

4. `IInvitationManager.HashToken` — `IInvitationManager.cs:43`. Drop from interface, make private.
5. `IBackupReader.FileExists` — `IBackupSection.cs:28`. **Delete.**
6. `IBackupReader.ListFilesInSection` — `IBackupSection.cs:29`. **Delete.**
7. `ICollectionRepository.GetMaxSortOrderAsync` — `ICollectionRepository.cs:20`. **Delete.**
8. `IDeviceRepository.GetDeviceByDeviceIdAsync` — `IDeviceRepository.cs:10`. **Delete.**
9. `IDeviceRepository.AddDeviceAsync` — `IDeviceRepository.cs:14`. **Delete.**
10. `IEmailDeliveryLogRepository.PruneOldAsync` — `IEmailDeliveryLogRepository.cs:11`. 0 callers (no pruning scheduled). **Delete or wire up.**
11. `IIptvRepository.ProfileHasDvrPermissionAsync` — `IIptvRepository.cs:29`. **Delete.**
12. `IIptvEpgService.GetSyncStats(Guid sourceId)` — `IptvEpgService.cs:20`. **Delete** (the all-sources variant IS used).
13. `IUserRepository.GetRegistrationModeAsync` — `IUserRepository.cs:15`. **Delete.**
14. `IMediaProvider.ValidateConnectionAsync` — `IMediaProvider.cs:7`. Interface itself has only 2 references total. **Verify entire interface dead; if so delete.**
15. `IMediaProvider.ReportPlaybackProgressAsync` — `IMediaProvider.cs:9`. Same.
16. `IAdminNotificationRepository.DeleteOlderThanAsync` — `IAdminNotificationRepository.cs:12`. **Delete or wire up.**
17. `ITranscodeService.StopTranscodeSessionAsync` — `ITranscodeService.cs:8`. Only called as local method inside impl. Drop from interface.
18. `ITaskQueueManager.QueueRefreshLibraryArtwork` — `TaskQueueManager.cs:30`. **Delete.**
19. `IServerPlaybackTracker.PruneExpired` — `IServerPlaybackTracker.cs:8`. Only local. Drop from interface.
20. `IVideoThumbnailManager.PurgeMediaItemThumbnailsAsync` — `IVideoThumbnailManager.cs:8`. Only local. Drop from interface.

### 7.3 Whole C# service that's dead

21. `IWebhookDispatcherService` / `WebhookDispatcherService` — `src/Vora.Application/Tracking/IWebhookDispatcherService.cs` + `src/Vora.Infrastructure/Notifications/WebhookDispatcherService.cs`. Registered in DI (`ServiceRegistrationExtensions.cs:363`) but never injected anywhere; `DispatchAsync` never called. **Delete service + interface + DI registration.**

### 7.4 Dead public methods on manager classes

22. `MusicRecommendationManager.RefreshWeeklyMixesForProfileAsync` — line 997. Only "all profiles" sibling is wired into `RecommendationRefreshWorker`. **Delete.**
23. `MusicManager.GetLikedTrackCountAsync` (+ `IMusicRepository.GetLikedTrackCountAsync` + impl) — chain of 3 declarations with 0 external callers. **Delete the chain.**

### 7.5 Dead frontend components

24. `src/Vora.Web/src/components/Admin/Primitives/Section.tsx` — 0 importers. **Delete.**
25. `src/Vora.Web/src/components/Client/Primitives/Glass.tsx` — referenced in `docs/redesign/design-language.md` as planned, no actual import. **Delete or wire up.**
26. `src/Vora.Web/src/components/Client/Primitives/Chip.tsx` — 0 importers. **Delete.**
27. `src/Vora.Web/src/components/Client/Shell/clientNavData.tsx` — exports `CLIENT_NAV`, `ClientIcons`, `resolveClientPath`, `ClientNavEntry`, `ClientIconName`; none imported anywhere. **Delete.**
28. `src/Vora.Web/src/components/YouTube/YouTubeChannelBadge.tsx` — 0 importers. **Delete.**

### 7.6 Stale documentation

29. `docs/admin-design-spec.md` — only mentioned in `src/Vora.Web/src/theme/types.ts:10` comment; describes done "Phase 1/Phase 2" plan. **Archive.**
30. `docs/admin-reorg-plan.md` — 0 references. Plan for already-shipped feature. **Archive.**
31. `docs/multi-server-audio-plan.md` — 0 references. **Archive.**
32. `docs/redesign/README.md`, `information-architecture.md`, `page-redesigns.md`, `rollout-plan.md` — not in CLAUDE.md index, only `client-templates.md`, `template-scheduling.md`, `design-language.md` are. **Verify which are live; archive the rest.**

### 7.7 ModalZIndex type permits unused below-header values

33. `src/Vora.Web/src/components/Common/Modal.tsx:4` — `'z-50' | 'z-[60]' | 'z-[100]'` members are never used by in-tree callers. Removing them enforces convention at compile time.

### 7.8 Confirmed not dead

34. `is_admin` localStorage key is fully purged (0 occurrences in `src/Vora.Web/src/**`).
35. Radarr/Sonarr calendar providers correctly have empty `GetSettingDefinitions()` (`RadarrCalendarProvider.cs:28`, `SonarrCalendarProvider.cs:28`).
36. All backend endpoints have at least one matching frontend caller; no orphan endpoints.

---

## 8. Testing — separately called out

There are **zero automated tests** in the repository — backend or frontend. With a 165k+ LOC codebase that includes auth, plugin loading, smart playlists, marker detection, backups, EPG sync, etc., this is the single highest-leverage finding in the entire review.

Recommended starter scope:

- `tests/Vora.Application.Tests` (xUnit + `Testcontainers.PostgreSql` or `EFCore.InMemory` for fast tests):
  - `AuthManager` — login, registration, password reset, invitation acceptance happy paths + ownership checks.
  - `SmartPlaylistEvaluator` — known rule trees against a seeded test DB.
  - `BackupSection` round-trips (each `IBackupSection` implementation).
  - `MediaAnalyzerManager` — marker lock behavior, season cluster snap.
- `tests/Vora.Plugins.Tests`:
  - `PluginLoaderExtensions` — multi-interface registration, version mismatch refusal.
- `src/Vora.Web` Vitest + React Testing Library:
  - `PlayerContext` — smoke tests for play/seek/skip.
  - `App.tsx` — route guards (especially `RequireAuth`, future `RequireAdmin`).
  - `useDialog` and `dialogs/Dialog.tsx` rendering.

Adding even minimal coverage in these areas is high-leverage: each test catches whole classes of regression that today would only surface in QA or prod.

---

## 9. What's done well (the positive ledger)

It's worth calling out what's working — these are the load-bearing structures of the project.

- **CLAUDE.md is comprehensive and clearly drives behavior.** The rules are concrete and the codebase visibly follows most of them.
- **Per-domain docs in `docs/` are short, focused, and current** for the linked subset.
- **Endpoint organization** in `src/Vora.Api/Endpoints/` is consistent and discoverable.
- **`Program.cs` is 14 lines.** Everything flows through `ServiceRegistrationExtensions`.
- **`AuthExtensions.cs`** centralizes claim access cleanly (with one violator noted above).
- **No `any`/`unknown`/`@ts-ignore`** in TypeScript. That's rare.
- **No native `alert/confirm/prompt`** in the frontend — dialog system is consistently used.
- **No direct axios** outside the service layer.
- **localStorage `is_admin` purge is complete** — `is_server_admin` is consistently canonical.
- **DataProtection setup is correct** (correctly persists to disk, app name set).
- **Migration history is clean** — 12 migrations over 2 days, sequential, no edits to prior migrations.
- **Radarr/Sonarr calendar provider unification** is fully reflected — no residual URL/API-key fields per CLAUDE.md guidance.
- **`docker-compose.yml` mounts `Vora-data` correctly** for DataProtection keys, logs, backups (per CLAUDE.md).
- **Plugin contract surface is large and consistent in shape**, even if it can be tightened.

---

## 10. Recommended action plan

The shortlist below is ordered by impact-vs-effort. Items in **bold** are the highest-leverage.

### Sprint 1 — security & deploy hygiene (1-3 days)

1. **Fix JWT placeholder + add startup validation** (2.1, 2.2). Refuse to boot with the default secret; validate `Issuer`/`Audience`.
2. **Add `.RequireAuthorization()` to DVR/HLS/SignalR endpoints + path-traversal containment** (2.4, 2.5, 2.6, 2.7, 2.8).
3. **Add ownership check to `PUT /api/users/{userId}`** (2.9).
4. **Switch `docker-compose.yml` default to `ASPNETCORE_ENVIRONMENT=Production`** (2.14). Document override.
5. **Move scoped fire-and-forget calls to `ITaskQueueManager`** (2.13).
6. **Add security headers + HSTS** (6.1.6).

### Sprint 2 — perf & data wins (3-5 days)

7. **Add HNSW index on `MediaItemEmbedding.Embedding`** (4.1 first item) — single highest-impact data finding.
8. **Switch to `AddDbContextPool`** (4.2).
9. **Add indexes on `MediaItem.{TmdbId,ImdbId,TvdbId}`, `AdminNotifications.CreatedAt`, `IptvRecordingSession.{StartTime,Status}`** (4.1).
10. **Memoize `PlayerContext` + split time slice** (2.15) — frontend's biggest perf win.
11. **Cache axios instances** (2.16).
12. **Add `React.lazy` to all routes in `App.tsx`** (5.5 first item).
13. **Tokenize `Modal.tsx`, `Dialog.tsx`, `MediaCard.tsx`** (5.2) — cascades to hundreds of palette hits.
14. **Add typed options pattern (`StoragePathsOptions`, `JwtOptions`)** (3.11).

### Sprint 3 — testing & observability (5-7 days)

15. **Stand up `Vora.Application.Tests` + Vitest** with the starter scope in section 8.
16. **Add `/health` endpoint + global exception handler** (6.2 first two items).
17. **Add `OpenTelemetry.Extensions.Hosting`** for tracing + a Prometheus exporter (6.2 third item).
18. **Add HTTP resilience (`AddStandardResilienceHandler`) to all external `IHttpClientFactory` clients** (6.4 first item).
19. **Add rate limiting on auth endpoints** (6.1.3).

### Sprint 4 — debt cleanup (2-3 days)

20. Dead code sweep (section 7) — 28 backend items + 5 frontend components + 4-6 doc archives.
21. **Reconcile layer-boundary docs vs reality** (2.10, 2.11) — pick a story.
22. **Replace `.ToLower().Contains(...)` with `EF.Functions.ILike` + `pg_trgm` indexes** (4.3).
23. **Add `CancellationToken` to plugin interfaces uniformly** (3.7).
24. **Split god-class managers** (3.3) — `MusicManager` and `MusicRecommendationManager`.
25. **Centralize localStorage keys** (5.8 first item).

### Sprint 5+ — bigger refactors

26. **Split `MusicTab.tsx`, `SettingsPage.tsx`, `LiveTvGuide.tsx`** into nested routes / sub-components (5.9).
27. **Move workers to `Vora.Infrastructure/Workers/`** (3.10) or update docs.
28. **Convert `List<Guid>`/`List<string>` JSON columns to native Postgres arrays** (4.6).
29. **Per-account SignalR groups instead of `Clients.All`** (3.9 second item).
30. **Adopt JWT `securityStamp` claim for live revocation** (2.3).

---

_End of review._
