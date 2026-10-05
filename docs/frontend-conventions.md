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
components/Home/          HomeCustomizeModal, SmartListRow
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
components/Player/Panels/ PlayerSettingsPanel, UpNextOverlay,
                          LiveTvInfoPanel, LiveTvRecordModal
components/Media/         MediaInfoDialog (Get info: shared by the player and the
                          media details page), FixMatchModal
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
pages/Admin/SmartLists/   SmartListsPage, SmartListEditorModal (+ smartListForm.ts)
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
pages/Client/Collections/ CollectionsPage (Home tab only), CollectionDetailsPage
pages/Client/Playlists/   PlaylistsPage, PlaylistDetailsPage
pages/Client/Discovery/   DiscoveryPage, DiscoveryActorPage,
                          DiscoveryDetailsPage, DiscoveryViewAllPage
pages/Client/LiveTv/      LiveTvPage, LiveTvGuide, DvrDashboard
```

Tab-host pages and the `embedded` prop — Home's tabs, Discover, For You, the watchlist — are in [`docs/frontend-client-ui.md`](frontend-client-ui.md).

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

The media tile system (`MediaCard`, `MediaRow`, `MediaGrid`, `DetailHero`, captions, sizing tokens) and the player shared primitives are in [`docs/frontend-client-ui.md`](frontend-client-ui.md).

The admin shell and its design tokens, the admin theme system, and the admin nav data are in [`docs/frontend-admin.md`](frontend-admin.md).

## Routing & state

- **Routing:** `react-router-dom`. Routes are wired in `App.tsx`.
- **Realtime:** `@microsoft/signalr` client → see `docs/realtime.md`.
- **Styling:** Tailwind (with PostCSS + Autoprefixer). Use `cursor-pointer` on every clickable element.
- **State management:** none formalized. Ask before introducing Redux/Zustand/React Query.
- **Other libs in use:** `hls.js` (HLS playback), `react-rnd` (resizable/draggable, used by `OverlayEditor`).

Remote-control (D-pad) navigation and row hover are in [`docs/frontend-client-ui.md`](frontend-client-ui.md).

## Lint must be clean

`npx eslint src` finishes with **no errors and no warnings**. There is no tolerated baseline — fix a problem where it is, including in files a change didn't otherwise touch, and don't silence a rule with an `eslint-disable` comment. The React Compiler rules catch real bugs here, not style:

- **`set-state-in-effect`** — resetting state when a prop changes. Derive it instead (`ArtImage` tracks *which* `src` failed; `LiveTvHubPage` computes the effective tab), key async results by what they were fetched for (`ActorDetailsPage` credits, `LibraryPage` recommendations, `MediaDetailsPage` quality media), or adjust during render behind a guard (`MediaDetailsPage` closing overlays when `id` changes). Setting state inside a promise callback is fine.
- **`exhaustive-deps`** — before adding a missing dependency, check that its identity is stable, or the effect re-runs. `DialogProvider` rebuilt its `api` object every render, so adding `dialog` to the live TV stream effect would have restarted the stream whenever any dialog opened; the fix was memoising the api, not skipping the dependency.
- **`purity`** — no `Date.now()` during render. `LiveTvGuide` keeps the current time in state on a one-minute interval and derives both the now line and the current-programme sort from it.

## Conventions you must follow

- **Strict TypeScript.** Never leave `any` or `unknown` in the code.
- **No `alert/confirm/prompt`.** Use `useDialog`.
- **`cursor-pointer` on every clickable element** (button, anchor, click handler on a div).
- All API calls through `apiClient` (or a service that wraps it).
- localStorage keys: see `docs/auth-and-devices.md`. Don't invent new ones in a vacuum. **`is_server_admin`** is the canonical admin flag — `is_admin` is dead.
- Device headers (`X-Vora-Device-Id`, `X-Vora-Client`, `X-Vora-Device`, `X-Vora-Device-Type`, `X-Vora-OS`) are set automatically in the `client.ts` interceptor. Don't set them by hand.
