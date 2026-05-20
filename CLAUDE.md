# Vora — Project Guide for Claude

This file is the entry point. It tells you how Vora is organized, links to deeper docs, and lists the small set of rules you must follow on every change.

## What Vora is

Vora is a self-hosted media server with a .NET backend (`Vora.Api`) and a React SPA (`Vora.Web`). It indexes a user's media library, surfaces it through a cinematic, template-themed client UI (Home, Library, Live TV, Discovery, etc.), and supports IPTV with DVR, multi-server clients, profiles, plugins, real-time updates over SignalR, and scheduled visual templates (e.g. a Thanksgiving look for a date range).

## Tech stack at a glance

- **Backend:** .NET 10, C#, minimal APIs (`Vora.Api`)
- **Database:** PostgreSQL with the `vector` extension, EF Core
- **Real-time:** SignalR hub at `/hubs/Vora`
- **Frontend:** React + TypeScript + Vite + Tailwind, axios, `react-router-dom`, `@microsoft/signalr`, `hls.js`, `react-rnd`
- **Containerization:** API always runs in Docker; FFmpeg lives inside the container
- **IDE:** Visual Studio 2026, single solution `Vora.slnx`

## Where to read more

Read the doc that matches what you're doing. Each file is kept short (≤200 lines) so the whole file fits in context.

- [`docs/architecture.md`](docs/architecture.md) — solution layout, project boundaries, dependency rules, where new code goes, build & run
- [`docs/backend-conventions.md`](docs/backend-conventions.md) — `Vora.Api` structure, endpoints pattern, managers/services/repos, C# style, naming
- [`docs/frontend-conventions.md`](docs/frontend-conventions.md) — `Vora.Web` structure, API service grouping, page layout, dialogs, shared primitives
- [`docs/database.md`](docs/database.md) — DbContext layout, migrations workflow, PostgreSQL/vector, column constraints
- [`docs/auth-and-devices.md`](docs/auth-and-devices.md) — auth flow, account/profile tokens, device tracking, claims, localStorage keys
- [`docs/realtime.md`](docs/realtime.md) — VoraHub, `IClientNotifier`, `useSignalREvent`
- [`docs/plugins.md`](docs/plugins.md) — plugin contracts, loader, provider categories
- [`docs/iptv-and-dvr.md`](docs/iptv-and-dvr.md) — IPTV playlists + EPG sources (split aggregates), EPG cache + matching strategy, DVR sessions, timeshift, recording schedules
- [`docs/music-and-audio.md`](docs/music-and-audio.md) — music domain (Artist/Album/Track), recommendation engine, stations + radio, lyrics, Last.fm, server playback tracker, audio quality + crossfade + EQ, the `playbackContextType` discriminator, audio hub layout
- [`docs/playlists.md`](docs/playlists.md) — `PlaylistMediaType`, manual playlists, smart playlists (rule tree JSON, evaluator architecture, fields-per-type matrix), Playlists page tabs + type-first creation flow
- [`docs/redesign/client-templates.md`](docs/redesign/client-templates.md) — client template system: token model, `data-vora-client` / `data-vora-admin` scoping, `ClientTemplateProvider`, built-in templates, plugin template bundles
- [`docs/redesign/template-scheduling.md`](docs/redesign/template-scheduling.md) — `ClientTemplateSchedule` entity, resolution chain (schedule → profile override → profile default → server default), seasonal template windows
- [`docs/redesign/design-language.md`](docs/redesign/design-language.md) — cinematic design language: client primitives (`Hero`, `CinematicBackdrop`, `MediaPoster`, `MediaRail`, `LetterRail`, `QualityPanel`, `Glass`, etc.), modal stacking, brand assets
- [`docs/adr/0001-split-iptv-playlist-from-epg-source.md`](docs/adr/0001-split-iptv-playlist-from-epg-source.md) — ADR: why playlists and EPG sources are independent aggregates and how matching/merging works

## Golden rules — apply on every change

These are project-wide. The deeper docs add their own rules; these never bend.

**Backend (C#)**

- Nullable reference types are **on**. Treat warnings as bugs. **Never** suppress with `!` or `#pragma`. Fix the null path.
- Async methods always use the `Async` suffix.
- The codebase is intentionally **comment-free**. Let names and structure carry meaning. XML doc comments are also off.
- `Vora.Domain` depends on nothing else in the solution. `Vora.Application` depends only on `Vora.Domain`. Implementations of repository interfaces live in `Vora.Infrastructure`.
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
