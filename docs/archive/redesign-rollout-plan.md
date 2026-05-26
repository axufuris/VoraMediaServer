# Phased rollout

Four phases, each independently mergeable. After every phase the app still runs and existing functionality keeps working — the new design rolls out behind the new primitives gradually rather than in one giant cutover.

## Phase 1 — Foundation (no visible UI change yet)

**Goal.** Build the design tokens, primitives, and the client template system end-to-end so subsequent phases drop into a finished platform.

Tickets:

1. **Tokens**: extend `styles/tokens.css` with the new motion variables, new background slot vars (`--vora-bg-glass`, `--vora-bg-player-scrim-image`, `--vora-bg-login-canvas-image`). Backfill the new color defaults on the default admin theme so admin pages stay unchanged.
2. **`components/Client/Primitives/`**: build `PageHeader`, `EmptyState`, `Tabs`, `Chip`, `Glass`, `MediaPoster`, `MediaStill`, `MediaRail`, `CinematicBackdrop`, `Hero`, `NowPlayingBar`, `QualityPanel`, `LetterRail`, `ScheduledTemplateBanner`. Each with TS-strict props, no `any`.
3. **`components/Client/Shell/`**: `ClientShell`, `Topbar`, `RailNav`, `clientNavData.tsx`, `UniversalSearchPalette`. Replaces `layouts/MainLayout.tsx` body — keep `MainLayout.tsx` as a thin wrapper that composes `ClientShell` for transition safety.
4. **Backend**: `Vora.Domain.Entities.Settings.ServerSetting.DefaultClientTemplateId`, new `UserProfile.ClientTemplateId`, `UserProfile.ScheduleOverrideTemplateId`, `UserProfile.ScheduleOverrideScheduleId`. New `ClientTemplateSchedule` entity. EF configuration. **Add migration via PMC: `add-migration AddClientTemplates`.**
5. **Backend application layer**: `IClientTemplateRegistry`, `IClientTemplateBundleLoader`, `IClientTemplateManager`, `IClientTemplateScheduleManager` + impls in `Vora.Infrastructure`. Register via `AddVoraManagers` / `AddVoraApplicationServices`.
6. **Backend endpoints**: `Vora.Api/Endpoints/TemplateEndpoints.cs` mapped in `WebApplicationExtensions.MapVoraEndpoints`.
7. **Backend SignalR**: `ClientTemplateChanged`, `ClientTemplateSchedulesChanged` on `IClientNotifier`. Implement in `SignalRClientNotifier`.
8. **Frontend providers**: `theme/ClientTemplateProvider.tsx`, `api/System/clientTemplateService.ts`. Mount provider in `App.tsx`.
9. **Built-in client templates**: `theme/clientTemplates/voraCinema.ts`, `voraNoir.ts`, `voraVelvet.ts`, `voraAurora.ts`. Register in `BUILT_IN_CLIENT_TEMPLATES` + backend registry.
10. **Tests**: resolution table, SetActive logic, schedule manager.

**Definition of done.** Existing client pages render identically (still using the old `MediaRow`, etc.). Admin can hit the new template endpoints. `ClientTemplateProvider` is mounted but applies the cinema default. New primitives compile and have at least one usage somewhere (Settings page placeholder is fine).

## Phase 2 — Hero pages

**Goal.** Land the cinematic feel on the three highest-impact pages.

Tickets:

1. **`HomePage.tsx`** — Hero + Continue Watching + dynamic rails. Replace `MediaRow` with `MediaRail`, `MediaCard` with `MediaPoster` / `MediaStill`. Wire `HomeCustomizeModal` into the new `PageHeader` action slot.
2. **`LibraryPage.tsx`** — Library hero strip, filter chips, posters grid, floating `LetterRail`, refactor admin 3-dot menu to themed `Menu`.
3. **`MediaDetailsPage.tsx`** — full-bleed `CinematicBackdrop`, hero card, tabbed content (Overview / Episodes / Cast / Extras / Similar), Play CTA, `QualityPanel` slide-in for tracks (replaces raw `<select>`s).
4. **Spotlight smart list flag.** Tiny backend tweak: add an `IsSpotlight` boolean on `SmartList` (or repurpose an existing tag) so an admin can mark one list as the Home hero source. Add migration `AddSmartListSpotlightFlag`. Frontend: surface in admin smart-list editor.

**Definition of done.** Home, Library, Media Details all look cinematic, use the new primitives, and respect the active template. `vora-noir` and `vora-velvet` templates are visibly different when switched.

## Phase 3 — Media surfaces

**Goal.** Bring Live TV, DVR, Music, and the player surfaces up to the new design.

Tickets:

1. **`LiveTvPage.tsx`** — On Now hero + channel grid.
2. **`LiveTvGuide.tsx`** — EPG visual refresh (timeline, roundels, glass popover, chip filters).
3. **`DvrDashboard.tsx`** — `Tabs`, `MediaStill` for sessions/folders, `useDialog` for series prompts.
4. **`AudioHubPage.tsx`** — Hero + Music/Radio/Podcasts tabs.
5. **`MusicTab.tsx`, `PodcastsTab.tsx`** — extracted rails, chips, primitive-driven layouts.
6. **`NowPlayingFullscreen.tsx`** — three-pane layout, ambient backdrop, primitive controls.
7. **`GlobalVideoPlayer.tsx` / `LiveTvPlayer.tsx`** — `Glass` chrome, `QualityPanel`, ambient mode toggle. Settings toggle added to Playback tab.
8. **`LiveRadioPlayer.tsx`** — compact `Glass` variant with visualizer band.
9. **`PlayerSurfaceProvider`** — unified mount point in `MainLayout` replacing the three conditional player renders.

**Definition of done.** Watching anything — video, music, radio, live, DVR — uses the new chrome and respects the template.

## Phase 4 — Polish + schedules + remaining pages

**Goal.** Everything else, plus the Templates Settings tab and the admin scheduling page.

Tickets:

1. **`SettingsPage.tsx`** — full restructure into tabs (Templates, Playback, Providers, Account, Devices, About).
2. **Templates tab** — gallery + `ScheduledTemplateBanner` + active-schedule-aware action buttons. Wired to `clientTemplateService`.
3. **Admin `pages/Admin/Templates/SchedulesPage.tsx`** — list grouped by Active/Upcoming/Past/Disabled, create/edit modal, validation. Added to `adminNavData.tsx`.
4. **`DiscoveryPage.tsx`** + `DiscoveryDetailsPage.tsx` + `DiscoveryActorPage.tsx` + `DiscoveryViewAllPage.tsx` — primitive sweep.
5. **`PlaylistsPage.tsx`** + details + smart details — primitive sweep, two-step create dialog.
6. **`CollectionsPage.tsx`** + `CollectionDetailsPage.tsx` — primitive sweep, backdrop on details.
7. **`WatchlistPage.tsx`** — primitive composition.
8. **`SearchPage.tsx`** — sectioned rails. Universal-search palette wiring (cmd-K from anywhere).
9. **`CalendarPage.tsx`** — token sweep, chips on events.
10. **`RecommendationsPage.tsx`** — primitive sweep.
11. **`ActorDetailsPage.tsx`** — cinematic hero + filmography rails.
12. **Auth pages** — `LoginPage`, `RegisterPage`, `SetupPage`, `ProfileSelectionPage` — pull `loginCanvas` from active template; restyled with primitives.
13. **Remove deprecated `MediaRow` / `MediaCard`** once nothing references them.
14. **`MainLayout.tsx`** — shrink to the thinnest possible wrapper around `ClientShell`. Drop the deprecated sidebar code.
15. **End-to-end check**: every page uses `PageHeader`, no raw `<select>` survives, no hand-rolled tabs survive, no `bg-[#1e1f22]` survives.

**Definition of done.** Every client page is on the new design. Switching templates (including a scheduled override) visibly changes the entire client. Admin can schedule a template for a date range and see it propagate live across other connected clients.

## Risks and dependencies

- **Strict TS + nullable backend.** New entities are nullable from the start; resolution logic always provides a fallback. The defaults baked into `ServerSetting` mean a fresh database with no setup ever renders something.
- **Theme provider ordering.** `ThemeProvider` (admin) and `ClientTemplateProvider` (client) must coexist. Both write to `:root` CSS variables — that is fine because each owns the `--vora-*` namespace on its respective surface. The risk is admin and client previewing simultaneously (e.g. an admin tab + client tab in one browser session, both using localStorage). Mitigate by namespacing the localStorage keys (`vora_admin_theme_id` vs `vora_client_template_id` — already distinct) and by reapplying the appropriate provider when the route changes between `/admin` and the client surface.
- **Migration discipline.** Two migrations introduced (`AddClientTemplates`, `AddSmartListSpotlightFlag`) — both via PMC, both in their own phase. Never hand-edit either.
- **Plugin authors.** Existing theme plugins keep working unchanged. Authoring a client template means dropping into `Templates/<id>/` instead of `Themes/<id>/` — document this in `docs/admin-theme-bundles.md` (rename the doc to `docs/theme-and-template-bundles.md` and cover both).

## Estimated effort (calendar days, single developer)

| Phase | Estimate |
| --- | --- |
| Phase 1 — Foundation | 8–10 days |
| Phase 2 — Hero pages | 5–7 days |
| Phase 3 — Media surfaces | 6–8 days |
| Phase 4 — Polish + schedules | 7–10 days |
| Total | ~26–35 days |

Most of the variance is in Phase 4 (lots of small pages) and the player chrome (ambient mode is the only piece that's research-y). Schedules themselves are a single day of focused work as called out in `template-scheduling.md`.
