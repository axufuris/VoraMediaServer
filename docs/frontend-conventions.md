# Frontend conventions (`Vora.Web`)

React + TypeScript + Vite + Tailwind. The frontend is independent of the .NET projects and talks to `Vora.Api` over HTTP.

## Top-level file map

```
src/
  api/                  HTTP services (grouped by domain, see below)
    client.ts           axios wrapper + per-server token routing
  components/           Reusable React components (grouped by feature)
  contexts/             React contexts (PlayerContext, ...)
  dialogs/              Modal dialog system (alert/confirm/prompt via useDialog)
  hooks/                Custom hooks (useSignalREvent, ...)
  layouts/              MainLayout (client sidebar + topbar shell), AuthLayout
                        Admin shell lives in components/Admin/Shell/AdminShell
  pages/                Route components (grouped by user role / feature)
  styles/
    tokens.css          Design-token CSS variables + .vora-* utility classes
  theme/                Admin theme system (manifests, ThemeProvider, applyTheme)
  utils/                serverVault, hardwareScanner, ...
  App.tsx               Router + routes registration. Wraps everything in
                        DialogProvider → PlayerProvider → BrowserRouter → ThemeProvider
  main.tsx              ReactDOM entry
```

## `src/api/` — services grouped by domain

API services are grouped by the backend resource they wrap:

```
api/client.ts             axios wrapper - stays at root
api/Auth/                 authService, invitationsAdminService
api/Users/                userService, userImageService, profileService,
                          profileDeviceSettingsService, deviceService
api/Media/                mediaService, libraryService, libraryAdminService,
                          artworkService, actorService, historyService, syncService
api/Collections/          collectionService, collectionAdminService,
                          playlistService, smartListService
api/Streaming/            streamingService, streamingAdminService, overlayService
api/Iptv/                 iptvClientService, iptvAdminService,
                          iptvEpgAdminService, dvrService,
                          dvrPlaybackService, timeshiftService
api/Discovery/            discoveryService, recommendationService, searchService,
                          requestAdminService, calendarService
api/System/               systemSettingsAdminService, emailAdminService,
                          pluginAdminService, taskService, aiStatsService,
                          adminService (dedupe), remoteAccessService,
                          adminNotificationService, featureFlagsService, themeService
```

Frontend services align 1:1 with backend `Vora.Api/Endpoints/*Endpoints.cs` groupings. When adding/splitting a service, mirror the backend grouping.

All HTTP calls go through `apiClient.get/post/put/delete` from `api/client.ts`. **Never** call axios directly from pages. `createServerClient` handles per-server vault/token routing automatically; pass `{ serverId }` in the config when targeting a non-active server.

## `src/components/` — grouped by feature

```
components/Common/        Modal, ArtworkPicker, IconSelect, RuleTreeEditor.
                          Modal supports a `surface="light"` variant for admin
                          modals that should pick up the active theme.
components/Layout/        SearchBar, ServerManagerModal
components/Media/         MediaEpisodesList, MediaExtrasRow,
                          RecommendationRow, EditMetadataModal,
                          MusicMetadataEditModal
components/Collections/   AddToCollectionModal, CreateCollectionModal,
                          EditCollectionModal, ReorderCollectionModal,
                          AddToPlaylistModal
components/Home/          HomeCustomizeModal
components/Discovery/     DiscoveryCustomizeModal
components/Admin/         AdminNotificationBell, UserAccessModal,
                          IptvChannelsModal, IptvPlaylistEditModal,
                          IptvEpgSourceEditModal, IptvEpgDiagnosticsModal
components/Admin/Shell/   AdminShell (replaces the old AdminLayout),
                          TopAppBar, SidebarV2, Breadcrumb, ServerSwitcher,
                          GlobalSearchTrigger, ActivityPill, AccountMenu,
                          SearchPalette (Cmd-K),
                          adminNavData.tsx (single source of truth — both
                          SidebarV2 and SearchPalette consume this list)
components/Admin/Primitives/
                          PageHeader, Section, StatCard, EntityCard, ListCard,
                          HealthBadge, StatusDot, EmptyState. These are what
                          admin pages compose against; don't hand-roll new
                          card / header / badge styles.
components/Admin/Features/
                          FeatureToggle (per-feature on/off pill),
                          FeaturePluginList (groups plugins by type with
                          accent section headers), FeatureTabs (admin
                          page sub-tabs)
components/Admin/Settings/CoreSettingsTab, PluginSettingsTab,
                          RemoteAccessTab, RequestServersTab
components/Player/        GlobalVideoPlayer, LiveTvPlayer
components/Player/Controls/ PlayerButtons (PlayPauseButton, SkipButton,
                          VolumeControl, FullscreenButton, MaximizeButton,
                          CloseButton), useAutoHideControls, useFullscreen
components/Player/Panels/ PlayerSettingsPanel, PlayerInfoPanel, UpNextOverlay,
                          LiveTvInfoPanel, LiveTvRecordModal
components/Iptv/          GuideProgramModal
components/Dvr/           DvrSessionCard
```

All folder names are PascalCase. Files use PascalCase for components, camelCase for hooks/utilities.

## `src/pages/` — grouped by user role

Filename prefixes (`Admin*`, `Client*`) have been stripped — the folder already conveys the role. Names like `HomePage`, `SettingsPage`, `MediaDetailsPage` may appear in multiple folders (admin vs client) — they're disambiguated by the import path.

```
pages/Auth/               LoginPage, RegisterPage, SetupPage
pages/Profile/            AccountSettingsPage, ProfileSelectionPage
pages/Admin/              DashboardPage, AiStatsPage, HistoryPage,
                          MusicHistoryPage, PluginsPage, RequestsPage,
                          SettingsPage, UserManagementPage, DedupePage,
                          AuthorizedDevicesPage, AppearancePage,
                          MediaTrashPage
pages/Admin/Libraries/    CreateLibrary, ManageLibrary
pages/Admin/SmartLists/   SmartListsPage
pages/Admin/Discovery/    DiscoveryPage
pages/Admin/Features/     ForYouPage, ReleaseCalendarPage, DvrPage
pages/Admin/Iptv/         IptvPage (renders Live TV + Internet Radio via prop)
pages/Admin/Podcasts/     PodcastsAdminPage
pages/Admin/Tasks/        TaskDashboard
pages/Admin/Overlay/      OverlayEditor
pages/Client/             HomePage, LibraryPage, LibraryDashboard, SearchPage,
                          SettingsPage, WatchlistPage, RecommendationsPage,
                          CalendarPage, ProfileHistoryPage
pages/Client/Media/       MediaDetailsPage, ActorDetailsPage
pages/Client/Collections/ CollectionsPage, CollectionDetailsPage
pages/Client/Playlists/   PlaylistsPage, PlaylistDetailsPage
pages/Client/Discovery/   DiscoveryPage, DiscoveryActorPage,
                          DiscoveryDetailsPage, DiscoveryViewAllPage
pages/Client/LiveTv/      LiveTvPage, LiveTvGuide, DvrDashboard
```

## Dialog system — replaces all `alert/confirm/prompt`

Use `useDialog()` from `src/dialogs/`. The provider is mounted globally in `App.tsx`.

```tsx
import { useDialog } from '../../dialogs';

const dialog = useDialog();

await dialog.alert('Saved.');
const ok = await dialog.confirm({
    title: 'Delete?', message: 'This cannot be undone.',
    confirmText: 'Delete', cancelText: 'Cancel', tone: 'danger'
});
const name = await dialog.prompt({ message: 'New name', defaultValue: '' });
```

Use this anywhere you'd otherwise reach for `window.alert/confirm/prompt`. **Never** use the native ones; they don't fit the player overlay z-index and look broken in our shell.

## Shared modal primitives

`Modal`, `ModalHeader`, `ModalBody`, `ModalFooter` from `components/Common/Modal.tsx` provide the overlay/card scaffold for all in-page modals (Edit Collection, Edit Metadata, etc.). They handle Escape key, optional backdrop dismiss, surface color, size, z-index, and overlay padding. Use them instead of hand-rolling `fixed inset-0` overlays.

**Z-index convention.** The MainLayout header is `z-[100]`. Modal overlays must sit above it. The `Modal` primitive defaults to `z-[200]` and supports `z-[210]` for nested modals. If you write a handwritten overlay anyway, use `z-[200]+`. Dialogs (`useDialog`) live at `z-[1000]`; full-screen players at `z-[99999]`.

**Token-only colors.** Modal contents use `var(--vora-bg-raised)`, `var(--vora-text-primary)`, `var(--vora-accent-500)`, etc. — never raw Tailwind palette classes. The `Modal` primitive itself accepts `surface="light"` for the admin-aware variant; client modals use the default surface.

`ArtworkPicker` from `components/Common/ArtworkPicker.tsx` is the shared poster/backdrop picker used by `EditMetadataModal` and `EditCollectionModal`. It handles upload, add-by-URL, delete, sort-current-selection-first, and provider-fetch via slot props.

## The media tile system

Everything that renders a piece of media as a tile goes through one small set of primitives in `components/Client/Primitives/`. There is exactly one implementation of each — do not hand-roll a card, a rail or a poster grid.

| Primitive | Use |
| --- | --- |
| `MediaCard` | **The** media tile: artwork plus the caption block under it. Home rails, Continue Watching, discover rows, recommendations, library/collection/watchlist/filmography grids, search results, playlists, seasons, collection tiles. |
| `MediaRow` | **The** horizontal rail. `variant="page"` sits on a page canvas and owns its gutter (`--vora-row-gutter`); `variant="section"` sits inside an already-padded block and gets an underlined header. Exports `MediaRowItem` for scroll-snap children. |
| `MediaGrid` | The wrapping-grid counterpart to `MediaRow`, on the same card metrics. |
| `PersonCard` | The cast/crew tile. |
| `CastRow` | `MediaRow` + `PersonCard`, with actors sorted ahead of crew. Used by both the media details page and the discovery details page. |
| `VideoCard` | The 16:9 trailer/extra tile. |
| `SectionHeader` | The heading shared by `MediaRow` and `MediaGrid`, so a rail title and a grid title match. |
| `DetailHero` | **The** top block of a detail page — backdrop, poster, title, chips, ratings, credits, actions, overview. Used by the media details page (Movie / TV Show / Season / Episode) and the discovery details page. `HeroChip` renders each hero fact; `HeroCredits` renders the director / genres / studio rows. |

**Captions.** What appears under a tile is decided by `utils/posterCaption.ts`, keyed on the item's `type` — Movie shows year + edition, Episode shows show / episode title / `S1 · E2`, Collection shows an item count, and so on. Pass `item={…}` to `MediaCard` and it captions itself. Only pass `title`/`captionLines` for tiles that aren't library media (a discovery result, a server-scoped search hit). Adding a new media type means adding a case in `posterCaption`, not a bespoke subtitle at the call site.

**Detail pages.** `MediaDetailsPage` serves all four local types — Movie, TV Show, Season, Episode — so they share one hero by construction; `posterShape="still"` and the `S1 E2` title suffix are the only per-type differences. The local media details page and the discovery details page render the same `DetailHero`, so a title looks identical whether it's in the library or not. They differ only in what they pass to `actions`: local items get Play / Quality & tracks / Watched / more, discovery items get Add to Watchlist and nothing else. Everything a page has data for goes into a slot — `eyebrow`, `chips`, `ratings`, `notice` — and slots a page has no data for are simply omitted. `DetailHero` lays the backdrop *beside* the content (right ~65% on md+) and dissolves it at the left and bottom edges via `CinematicBackdrop`'s `mask="edge"`, so the artwork melts into the page instead of ending on a hard line.

**Sizing.** Card metrics are tokens in `styles/tokens.css`: `--vora-card-w-sm|md|lg`, `--vora-card-min-w`, `--vora-card-gap`, `--vora-card-title-size`, `--vora-card-caption-size`, `--vora-card-badge-size`, `--vora-person-w`, `--vora-video-w`, `--vora-row-title-size`, `--vora-row-gutter`. The widths are `clamp(rem, vw, rem)` so a tile follows both the root font size and the viewport instead of snapping at breakpoints. Change a token and every rail, grid and detail page moves together. Never size a tile in `px`.

## Player shared primitives

`components/Player/Controls/PlayerButtons.tsx` exports `PlayPauseButton`, `SkipButton`, `VolumeControl`, `FullscreenButton`, `MaximizeButton`, `CloseButton`. Both `GlobalVideoPlayer` and `LiveTvPlayer` use these.

`useAutoHideControls({ isMinimized, isPlaying, keepVisibleWhen })` and `useFullscreen(containerRef)` factor out the mousemove auto-hide effect and fullscreen toggle. Use them in any future player surface.

## Admin shell + design tokens

The admin section (`/admin` and `/server/:serverId/admin`) is rendered inside `components/Admin/Shell/AdminShell.tsx`, which composes the top app bar, the sidebar (`SidebarV2`), and the routed page outlet. The old `layouts/AdminLayout.tsx` is a deprecated stub (look for `// DEPRECATED` or `Safe to git rm`) and is no longer referenced.

**Design tokens.** All admin colors, spacing, radii, shadows, motion, and layout dimensions are CSS variables defined in `styles/tokens.css`. They're prefixed `--vora-*` (`--vora-bg-canvas`, `--vora-text-primary`, `--vora-accent-500`, `--vora-radius-md`, etc.). The `:root` block in `tokens.css` is a first-paint fallback only — the active theme overrides them via JS once `ThemeProvider` mounts.

**Semantic utility classes** in `tokens.css` for hand-authoring:

- `.vora-card`, `.vora-card-interactive` — admin card surfaces (with hover lift)
- `.vora-button-primary`, `.vora-button-secondary` — accent and neutral buttons
- `.vora-input` — text/number/select inputs
- `.vora-page-header` — the sticky page-header strip (used by `PageHeader` primitive)
- `.vora-skeleton` — the shimmering load placeholder

Prefer these over hand-rolling `bg-[var(--vora-...)]` chains; reach for the `var(--vora-*)` directly only when composing inside another Tailwind expression.

**`data-vora-page` marker.** Every admin page (and `pages/Client/LibraryDashboard`, which is admin-flavored) wraps its root in `<div data-vora-page="">`. That's a value-less marker that scopes the page-surface CSS rule in `tokens.css` to "this is the page's primary surface, paint it with `--vora-bg-canvas`." Don't omit it — the page will render with no background.

**Building an admin page.** Compose with `Primitives/`: `PageHeader` (title + actions strip), `StatCard` / `EntityCard` / `ListCard`, `HealthBadge`, `StatusDot`, `EmptyState`. Group inputs into sections with the `vora-card p-6` surface. The `FeatureTabs` primitive handles tabbed sub-sections inside a page.

## Admin theme system

`ThemeProvider` from `theme/ThemeProvider.tsx` is mounted inside `BrowserRouter` in `App.tsx` and exposes `useTheme()` with `{ active, builtInThemes, isLoading, isSwitching, setActive }`. Built-in manifests live in `theme/themes/` (`voraDefault.ts`, `voraDark.ts`, `voraOcean.ts`); the schema is in `theme/types.ts`.

On mount, the provider:
1. Applies localStorage / URL-param / default manifest to `:root` for the first paint.
2. Calls `/api/admin/themes/active` to learn the server's persisted theme.
3. If it's a plugin theme (not bundled), fetches `/api/admin/themes/{id}/manifest`, hydrates `assetsBaseUrl`, applies it.

`setActive(id)` is async — optimistically applies + writes localStorage + POSTs to the backend; reverts on backend failure. Live propagation across browsers is via the `AdminThemeChanged` SignalR event.

URL escape hatch: append `?theme=<id>` to any URL to preview without persisting.

Authoring a new built-in theme: see the row in `docs/architecture.md`. Plugin-shipped themes don't touch frontend code at all — see `docs/admin-theme-bundles.md`.

## Admin nav data (single source of truth)

`components/Admin/Shell/adminNavData.tsx` exports `ADMIN_NAV` (the canonical list of admin pages with `label`, `pathTemplate`, `icon`, `section`, optional `keywords` for the palette, optional `requires: 'ai'` runtime gate). Both `SidebarV2` and `SearchPalette` consume this list. **Add a new admin page in exactly one place** — it will appear in the sidebar with the right icon/section and in the Cmd-K palette with the right keywords automatically. Same file exports `Icons` (the SVG-path map) and `resolveAdminPath(template, serverId)` (the `/admin/...` → `/server/<id>/admin/...` rewriter).

## Routing & state

- **Routing:** `react-router-dom`. Routes are wired in `App.tsx`.
- **Realtime:** `@microsoft/signalr` client → see `docs/realtime.md`.
- **Styling:** Tailwind (with PostCSS + Autoprefixer). Use `cursor-pointer` on every clickable element.
- **State management:** none formalized. Ask before introducing Redux/Zustand/React Query.
- **Other libs in use:** `hls.js` (HLS playback), `react-rnd` (resizable/draggable, used by `OverlayEditor`).

## Conventions you must follow

- **Strict TypeScript.** Never leave `any` or `unknown` in the code.
- **No `alert/confirm/prompt`.** Use `useDialog`.
- **`cursor-pointer` on every clickable element** (button, anchor, click handler on a div).
- All API calls through `apiClient` (or a service that wraps it).
- localStorage keys: see `docs/auth-and-devices.md`. Don't invent new ones in a vacuum. **`is_server_admin`** is the canonical admin flag — `is_admin` is dead.
- Device headers (`X-Vora-Device-Id`, `X-Vora-Client`, `X-Vora-Device`, `X-Vora-Device-Type`, `X-Vora-OS`) are set automatically in the `client.ts` interceptor. Don't set them by hand.
