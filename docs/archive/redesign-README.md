# Vora client redesign — plan

This folder is the design plan for the full redesign of the **client** side of Vora. The goal is a cinematic, immersive media-server experience that surpasses Plex, with a client-side template system that mirrors the existing admin theme system and adds admin-scheduled overrides (e.g. Thanksgiving template).

Read in order. Each doc is ≤200 lines so it fits in context.

## Documents

1. [`design-language.md`](design-language.md) — design system: color, typography, motion, density, the new `components/Client/Primitives/` library, how cinematic backdrops are handled.
2. [`information-architecture.md`](information-architecture.md) — new nav, page inventory, what stays / what merges / what is replaced.
3. [`page-redesigns.md`](page-redesigns.md) — page-by-page redesign for Home, Library, Discovery, Media Details, Live TV, DVR, Music / Audio Hub, Playlists, Collections, Watchlist, Search, Calendar, Settings, Player, Music Now Playing.
4. [`client-templates.md`](client-templates.md) — client template data model, endpoints, manifest schema (reuses `ThemeManifest`), resolution algorithm, frontend integration, SignalR.
5. [`template-scheduling.md`](template-scheduling.md) — the scheduled-template feature: entity, admin UI, resolution rules, "user override sticks until schedule ends, then reverts" behavior.
6. [`rollout-plan.md`](rollout-plan.md) — phased breakdown with concrete tickets per phase and definition of done.

## Mockups

[`mockups/`](mockups/) — open these in a browser at full size:

- `home.html` — new cinematic Home with featured hero + dynamic rows.
- `media-details.html` — full-bleed backdrop, Play CTA, polished track-quality panel.
- `templates-settings.html` — the new client Templates settings page (with scheduled-template banner).

## What this is and is not

This plan is **the source of truth for the redesign**. Implementation tickets reference this plan. When something in the plan turns out to be wrong during implementation, update the plan first, then write the code.

This plan is **not** a pixel-perfect Figma. It defines the design language, IA, primitives, data model, and rollout order. Page-level pixel details are decided per-page during implementation, anchored on the design system in `design-language.md`.

## Executive summary

**Aesthetic direction**: cinematic & immersive. Deep near-black canvas, full-bleed hero artwork on key pages, generous backdrops behind detail pages, smooth crossfades and parallax on motion. Two card shapes: poster (2:3) for movies/TV/albums, still (16:9) for episodes/Live TV/playlists. Persistent collapsible left rail nav + glass topbar with universal search.

**Template system**: parallel to the existing admin theme system. Same `ThemeManifest` schema (already supports `backgrounds.canvas`, `backgrounds.pageHeader`, plus we add `playerScrim` and `loginCanvas` slots). New endpoints under `/api/templates/*`. Per-profile selection persisted on `Profile`. Admin picks the server-wide default. Both built-in and plugin-shipped templates supported (plugin layout: `<install>/Templates/<id>/`).

**Scheduled templates**: feasible — about a day of backend work plus an admin UI. New `ClientTemplateSchedule` entity (id, templateId, startsAt, endsAt, name, priority, enabled). Resolution per request: active schedule → profile's override-within-this-schedule (if any) → profile default → admin default. When a profile manually picks a different template during a schedule window, that pick is stored against the schedule id; when the schedule ends, the override naturally becomes stale and resolution falls back to the profile's long-term default. No background worker required for correctness.

**Foundation work** done before any page redesign:

- New `components/Client/Primitives/` library: `PageHeader`, `CinematicBackdrop`, `MediaPoster`, `MediaStill`, `MediaRail` (replaces `MediaRow`), `Hero`, `EmptyState`, `Tabs`, `Chip`, `QualityPanel`, `Glass` (frosted surface).
- New `theme/ClientTemplateProvider.tsx` modeled on `ThemeProvider.tsx` — same lifecycle (first-paint cache, fetch active, apply CSS variables, SignalR reconcile).
- New `ClientTemplate*` types in `Vora.Application/Templates/` and endpoints in `Vora.Api/Endpoints/TemplateEndpoints.cs`.

**Rollout**: 4 phases. (1) Foundation: design tokens, primitives, template system end-to-end. (2) Hero pages: Home, Library, Media Details. (3) Media surfaces: Live TV, DVR, Music, Player. (4) Polish: Discovery, Playlists, Collections, Watchlist, Search, Calendar, Settings, scheduling UI. Each phase is mergeable on its own.

## Conventions this plan respects

- Backend layering rules from `docs/architecture.md` and `docs/backend-conventions.md`.
- Frontend folder structure from `docs/frontend-conventions.md` (services in `api/<Domain>/`, primitives in `components/Client/Primitives/`).
- Tokens are CSS variables prefixed `--vora-*` in `styles/tokens.css`.
- All API responses are `*VM` types from `Vora.Application`. No domain entities cross the API boundary.
- No `alert/confirm/prompt` — use `useDialog`.
- Migrations are added via VS PMC `add-migration` — never hand-edited.
