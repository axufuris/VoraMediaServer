# Vora — Project Guide for Claude

This file is the entry point. It tells you how Vora is organized, links to deeper docs, and lists the small set of rules you must follow on every change.

## What Vora is

Vora is a self-hosted media server with a .NET backend (`Vora.Api`) and a React SPA (`Vora.Web`). It indexes a user's media library, surfaces it through a cinematic, template-themed client UI (Home, Library, Live TV, Discovery, etc.), and supports IPTV with DVR, multi-server clients, profiles, plugins, real-time updates over SignalR, and scheduled visual templates (e.g. a Thanksgiving look for a date range).

## Tech stack at a glance

- **Backend:** .NET 10, C#, minimal APIs (`Vora.Api`)
- **Database:** PostgreSQL with the `vector` extension, EF Core
- **Real-time:** SignalR hub at `/hubs/Vora`
- **Email:** MailKit SMTP, ASP.NET Core DataProtection for SMTP password encryption
- **Frontend:** React + TypeScript + Vite + Tailwind, axios, `react-router-dom`, `@microsoft/signalr`, `hls.js`, `react-rnd`
- **Containerization:** API always runs in Docker; FFmpeg lives inside the container
- **IDE:** Visual Studio 2026, single solution `Vora.slnx`

## Where to read more

Read the doc that matches what you're doing. Each file is kept short (≤200 lines) so the whole file fits in context.

- [`docs/architecture.md`](docs/architecture.md) — solution layout, project boundaries, dependency rules, where new code goes, build & run
- [`docs/backend-conventions.md`](docs/backend-conventions.md) — `Vora.Api` structure, endpoints pattern, managers/services/repos, C# style, naming
- [`docs/frontend-conventions.md`](docs/frontend-conventions.md) — `Vora.Web` structure, API service grouping, page layout, dialogs, shared primitives
- [`docs/database.md`](docs/database.md) — DbContext layout, migrations workflow, PostgreSQL/vector, column constraints
- [`docs/auth-and-devices.md`](docs/auth-and-devices.md) — auth flow, account/profile tokens, device tracking, claims, localStorage keys, forgot-password + invitation flows
- [`docs/email.md`](docs/email.md) — SMTP transport, template renderer + admin overrides, dispatch worker, delivery log, DataProtection key storage, three use cases (password reset, admin invite, request fulfilled)
- [`docs/realtime.md`](docs/realtime.md) — VoraHub, `IClientNotifier`, `useSignalREvent`, coalesced admin streams (`LogEntryBatch`, `BackupCreated`, `BackupRestored`)
- [`docs/plugins.md`](docs/plugins.md) — plugin contracts, loader, provider categories, `IPluginSettingsProvider`, `IRequestServerLookup`, env-seeding mechanism
- [`docs/logs.md`](docs/logs.md) — in-memory ring buffer + file sink, runtime level overrides, admin Logs page, SignalR live tail
- [`docs/backups.md`](docs/backups.md) — `IBackupSection` model, atomic restore via `IBackupTransactionFactory`, scheduling + retention, admin Backups page
- [`docs/scanning-and-tasks.md`](docs/scanning-and-tasks.md) — background task queue (`ITaskQueueManager` + `TaskProcessingWorker`), per-task cancellation, per-item progress (`ITaskProgressReporter`), lazy task-name resolution, **parallel per-unit scan+enrich** full-library workflow (`DiscoverScanUnitsAsync` → `ScanAndEnrichUnitAsync` in one shared scope → deferred overlays/actors/analysis; `ReferenceWriteGate` for parallel-safe shared rows), folder watcher → per-file ingest (`QueueScanNewFile`) + `ExcludeFilters`, single-file scanner methods + new-season metadata refresh, duplicate-item prevention, per-part `Edition` + version picker, soft-delete / Media Trash + auto-purge, live `EpisodeCount`
- [`docs/artwork-image-cache.md`](docs/artwork-image-cache.md) — server-side resized artwork cache (`GET /api/artwork/thumb`, `IArtworkThumbnailService`, width buckets, 512 MB cap, source allowlist, `imagecache/` under `CustomArtwork`, orphan cleanup via `RemoveThumbnailsForSource`, `thumbUrl()` helper) and poster overlay badges (`PosterOverlayManager`, best-part resolution/video, best-audio-across-parts, regen gate). **Distinct from `docs/video-thumbnails.md`.**
- [`docs/iptv-and-dvr.md`](docs/iptv-and-dvr.md) — IPTV playlists + EPG sources (split aggregates), EPG cache + matching strategy, DVR sessions, timeshift, recording schedules
- [`docs/analysis-and-markers.md`](docs/analysis-and-markers.md) — silence + black-frame detection, marker assembly (Intro / Recap / Preview / Credits / CreditsScene), TV season clustering, per-profile auto-skip, admin tuning knobs, manual marker editor + `LockedFields["Markers"]` lock, library marker-coverage dashboard, player UX
- [`docs/video-thumbnails.md`](docs/video-thumbnails.md) — scrub-bar preview thumbnails: sprite + WebVTT pipeline via FFmpeg, `StoragePaths:VideoThumbnails` location, `VideoThumbnailScheduleTime` server-wide cadence, `LockedFields["Thumbnails"]` lock, admin coverage card + per-item regenerate, `VideoThumbnailsReady` SignalR event, player hook + overlay
- [`docs/music-and-audio.md`](docs/music-and-audio.md) — music domain (Artist/Album/Track), recommendation engine, stations + radio, lyrics, Last.fm, server playback tracker, audio quality + crossfade + EQ, the `playbackContextType` discriminator, audio hub layout
- [`docs/playlists.md`](docs/playlists.md) — `PlaylistMediaType`, manual playlists, smart playlists (rule tree JSON, evaluator architecture, fields-per-type matrix), Playlists page tabs + type-first creation flow
- [`docs/collections.md`](docs/collections.md) — collection content sync + chronological ordering: the two-provider model (`ContentSyncExternalId` vs `ExternalListId`), AI List (`openai_list`) franchise/universe scope + completeness passes, `CollectionMembershipResolver` title matching + **all-library-seasons expansion**, mirror mode + `ExcludedMediaIdsJson` + `ManuallyAdded`, empty-sync admin alerts, chronology (`openai_chronology`) setYear passes (seed → batch → verify → anchor → repair → distinct) + `InUniverseYear`/`InUniverseYearLocked` per-item year lock, what to type in each describe field, per-collection task serialization by `CollectionKey`
- [`docs/youtube.md`](docs/youtube.md) — YouTube plugin, three-tier access chain, parental controls (safeSearch + ytRating + ceiling), API client + caching TTLs, subscription feeds via RSS, `YouTubeAccessChanged` SignalR push, frontend pages + admin surface
- [`docs/redesign/client-templates.md`](docs/redesign/client-templates.md) — client template system: token model, `data-vora-client` / `data-vora-admin` scoping, `ClientTemplateProvider`, built-in templates, plugin template bundles
- [`docs/redesign/template-scheduling.md`](docs/redesign/template-scheduling.md) — `ClientTemplateSchedule` entity, resolution chain (schedule → profile override → profile default → server default), seasonal template windows
- [`docs/redesign/design-language.md`](docs/redesign/design-language.md) — cinematic design language: client primitives (`Hero`, `CinematicBackdrop`, `MediaPoster`, `MediaRail`, `LetterRail`, `QualityPanel`, `Glass`, etc.), modal stacking, brand assets
- [`docs/adr/0001-split-iptv-playlist-from-epg-source.md`](docs/adr/0001-split-iptv-playlist-from-epg-source.md) — ADR: why playlists and EPG sources are independent aggregates and how matching/merging works
- [`docs/adr/0002-client-platform-strategy.md`](docs/adr/0002-client-platform-strategy.md) — ADR: native clients per ecosystem (Swift/SwiftUI for Apple, Kotlin/Compose for Android, BrightScript for Roku later); shared contract layer (OpenAPI codegen + emitted design tokens) rather than shared renderer
- [`docs/clients/design-tokens.md`](docs/clients/design-tokens.md) — token source of truth (`ThemeManifest` TS type), `emit-tokens.ts` script, Swift + Kotlin emitter outputs at `dist/tokens/<themeId>/`, web vs native consumption models
- [`docs/clients/primitive-specs.md`](docs/clients/primitive-specs.md) — cross-platform contract for cinematic primitives (`Hero`, `CinematicBackdrop`, `MediaPoster`, `MediaRail`, `Glass`, `QualityPanel`, `NowPlayingBar`, …); prop shapes, observable behavior, TV focus rules
- [`docs/clients/openapi-codegen.md`](docs/clients/openapi-codegen.md) — generating Swift (`swift-openapi-generator`) and Kotlin (`openapi-generator` + Retrofit) clients from `Vora.Api`'s OpenAPI doc; Swashbuckle hardening, auth middleware, regeneration workflow

## Golden rules — apply on every change

These are project-wide. The deeper docs add their own rules; these never bend.

**Backend (C#)**

- Nullable reference types are **on**. Treat warnings as bugs. **Never** suppress with `!` or `#pragma`. Fix the null path.
- Async methods always use the `Async` suffix.
- The codebase is intentionally **comment-free**. Let names and structure carry meaning. XML doc comments are also off.
- `Vora.Domain` depends on nothing else in the solution (the base `Pgvector` value-type package is allowed for the `MediaItemEmbedding` entity, but the EF Core variant is not). `Vora.Application` depends only on `Vora.Domain` and `Vora.Plugins` — `Vora.Plugins` defines the provider contracts Application code consumes (`IMetadataProvider`, `IArtworkProvider`, `IMediaIngestionService`, etc.). Implementations of repository interfaces live in `Vora.Infrastructure`.
- Endpoints stay thin: parse → call manager/service → return. Heavy logic goes into a Manager/Service in `Vora.Application`.
- Claims access **always** goes through `Vora.Api/Extensions/AuthExtensions.cs` (`GetProfileId`, `GetAccountId`, `HasAllLibraryAccess`, …). Never call `user.FindFirst("...")` directly.
- HTTP clients use `IHttpClientFactory`. No static `HttpClient`.
- API responses must use View Models (`*VM`) or Response classes from `Vora.Application`. Never expose `Vora.Domain` entities through the API.
- DI registration goes in the matching helper inside `Vora.Api/Extensions/ServiceRegistrationExtensions.cs`. Never inline registration in `Program.cs`.
- Enums are serialized as **strings** at the HTTP boundary. The global `JsonStringEnumConverter` is registered in `AddVoraJsonOptions`. Don't add enum values that depend on integer ordinals; new enums work out of the box.

**Frontend (TypeScript / React)**

- TypeScript linter is strict. **Never** leave `any` or `unknown` in the code.
- **Never** use `alert()`, `confirm()`, or `prompt()`. Use the dialog system: `const dialog = useDialog(); await dialog.alert(...)`. See `docs/frontend-conventions.md`.
- Routing lives in `App.tsx`. State management: nothing formalized — ask before introducing Redux/Zustand/React Query.
- All API calls go through the per-domain services under `src/api/<Domain>/` (e.g. `api/Media/mediaService.ts`). Do not call axios directly from pages.
- localStorage keys are documented in `docs/auth-and-devices.md`. Don't invent new ones in a vacuum.
- **Colors come from tokens, not Tailwind palette.** Use `var(--vora-bg-canvas)`, `var(--vora-accent-500)`, etc. — never `bg-gray-900`, `text-orange-500`, `text-white`. Tokens are scoped by `data-vora-client="" ` (set on `MainLayout` and `AuthLayout`) and `data-vora-admin` (set on admin shell). New pages that live outside those wrappers should add `data-vora-client=""` themselves (see `ProfileSelectionPage`).
- **Modals must overlay the header.** The header is `z-[100]`. The `Modal` primitive defaults to `z-[200]`. Raw `<div className="fixed inset-0 …">` modal overlays must use `z-[200]` or higher.
- **Live permissions (Live TV / DVR) are refetched live**, not read from JWT alone — the JWT is stale after admin edits. See `LiveTvPlayer` for the `userService.getUserAccount()` refresh pattern.

**Build & run**

- The API **always** runs in Docker (FFmpeg is in the container). Don't try to run `Vora.Api` natively.
- Start everything from Visual Studio: set `docker-compose.dcproj` as the startup project and run it. If the UI isn't already up, `cd src/Vora.Web && npm run dev` first.

## Things to be careful about

- **Comments**: don't add them, even XML doc comments. If a method needs explanation, rename or restructure it.
- **Migrations**: never edit a checked-in migration. Add a new one via `add-migration FooName` in the Visual Studio Package Manager Console.
- **localStorage keys**: see `docs/auth-and-devices.md` — the canonical admin flag is `is_server_admin`, NOT `is_admin` (that key is dead). Per-profile home prefs use the `vora_show_spotlight_${profileId}` key (broadcast a `vora:home-prefs-changed` event when changing).
- **Header conventions**: device headers are all `X-Vora-*` (Device-Id, Client, Device, Device-Type, OS). The frontend interceptor in `src/api/client.ts` sends them; the backend `DeviceTrackingMiddleware` reads them.
- **Brand assets**: `public/favicon.svg`, `public/vora-logo.svg`, `public/vora-mark.svg`. The chevron-V wordmark is gradient-driven (`--vora-accent-text` → `--vora-accent-500`), so the brand recolors per template. Don't replace these with raster art.
- **DataProtection keys**: SMTP password is encrypted via ASP.NET Core DataProtection (`AddVoraEmail` in `ServiceRegistrationExtensions`). Keys persist to `StoragePaths:DataProtection` (default `<base>/DataProtectionKeys`). **Mount this directory as a Docker volume** — otherwise keys roll on every container rebuild and the saved SMTP password becomes undecryptable. See `docs/email.md`.
- **Storage paths added since the original deployment**: `StoragePaths:Logs` (file sink under `<base>/logs`, see `docs/logs.md`) and `StoragePaths:Backups` (zip backups under `<base>/backups`, see `docs/backups.md`). The default `docker-compose.yml` puts both under the existing `./Vora-data:/app/data` mount so they survive container rebuilds. The **resized artwork cache** adds no new path — it writes under the existing `StoragePaths:CustomArtwork` at `imagecache/` and self-bounds at 512 MB (see `docs/artwork-image-cache.md`).
- **Media Trash (soft-delete) is video-only**: when a video file disappears the `MediaItem` is stamped `MissingSince` and hidden from client reads rather than deleted; music `Track`s still hard-delete. Nightly auto-purge is gated on `ServerSetting.EnableTrashAutoPurge` + `MissingMediaRetentionDays`, and a purge archives per-profile ratings + watch-state into `PreservedUserMediaData` (keyed by `ContentIdentity.Compute`) so a later re-add restores them. See `docs/scanning-and-tasks.md` and `docs/auth-and-devices.md`.
- **Scrub-bar thumbnails vs the artwork cache are different subsystems**: `docs/video-thumbnails.md` is per-video WebVTT sprite sheets for the scrub bar; `docs/artwork-image-cache.md` is the resized poster/still/backdrop cache + poster overlay badges. Don't conflate `IVideoThumbnailService` with `IArtworkThumbnailService`.
- **A library scan's unit scans AND enriches in ONE DI scope**: `ScanAndEnrichUnitAsync` resolves the scanner + metadata manager from the same scope so enrichment writes onto the rows the scan just created. **Never** enrich in a separate `DbContext` from the scan that created the rows — that concurrent-context clobber is what silently blanked season posters (`ProcessTvSeasonsAsync` writes season posters during the show refresh). Parallel units are safe because each gets its own scope; shared rows (actors/genres/companies/…) are serialized by the singleton `ReferenceWriteGate` in `MetadataMappingService`. See `docs/scanning-and-tasks.md`.
- **Plugin settings can be bootstrapped from env vars**: `Vora__PluginSettings__<pluginId>__<settingKey>` env vars are read by `PluginSettingsEnvSeeder` at startup. Seed-once semantics — DB values take precedence. Plugin id + setting key are validated against installed plugins; unknown keys are skipped with a warning. Values are redacted from logs. Full per-plugin matrix lives in the README. See `docs/plugins.md` for the implementation.
- **Radarr/Sonarr calendar credentials are NOT in plugin settings**: as of the Request Server unification, `radarr_calendar` and `sonarr_calendar` providers have empty `GetSettingDefinitions()` and resolve credentials through `IRequestServerLookup`, which reads `RequestServer` rows where `ProvidesReleaseCalendar = true`. The Release Calendar admin page is now a toggle + a pointer to System Settings → Request Servers. Don't reintroduce URL/API-key fields on those plugins.
- **Profile-scoped overrides for plugin behavior**: where a plugin setting can legitimately differ per profile, prefer adding the field to `UserProfile` and reading it in the endpoint before falling back to the plugin's admin default. Example: `UserProfile.ShowtimesLocation` overrides `serpapi_theater.default_location` (see `Vora.Api/Endpoints/DiscoveryEndpoints.cs` for the precedence pattern).
- **Multi-tier access gates**: features that need server/account/profile resolution (like YouTube) follow a shared shape — resolver service in `Vora.Application/<Feature>/`, `EnsureAccessAsync` called at the top of every manager method (throws `UnauthorizedAccessException` → endpoint catches → 403), nav visibility computed from a `/settings` endpoint that returns the same `isAvailable` boolean the manager uses. When admins change account-level access, fire a SignalR event (e.g. `YouTubeAccessChanged`) that re-runs the resolution in `MainLayout` and any settings sections so nav items appear/disappear live without a profile re-select. See `docs/youtube.md`.
- **Marker locks reuse `LockedFields`**: admin-manual marker edits are protected by adding the literal string `"Markers"` to `MediaItem.LockedFields` (the existing `LockableEntity` pattern). `MediaAnalyzerManager.RunMediaItemSilenceDetectionAsync` and `FinalizeSeasonMarkersAsync` both bail early on `AreMarkersLockedAsync` — this means library/per-item analyze and the season cluster snap all respect the lock. Admin "Analyze media" clicks still force-override the trigger gate but still honor the lock; admins must unlock first to re-detect. See `docs/analysis-and-markers.md`.
