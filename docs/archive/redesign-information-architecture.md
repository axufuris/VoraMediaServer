# Information architecture

The new client keeps Vora's feature surface intact but reorganizes the chrome and consolidates duplicated patterns into shared primitives.

## Chrome

```
┌──────────────────────────────────────────────────────────────────┐
│  Topbar (Glass, sticky, 56px)                                    │
│  Logo · Library switcher · ─── universal search ──── · ⊙ profile │
├────┬─────────────────────────────────────────────────────────────┤
│ R  │                                                             │
│ a  │  Page content (full-bleed when the page wants it)           │
│ i  │                                                             │
│ l  │                                                             │
│    │                                                             │
└────┴─────────────────────────────────────────────────────────────┘
```

**Left rail** (`72px` collapsed, `260px` expanded on hover) replaces today's `MainLayoutSidebar`. The reorder/pin behavior we already have stays, but visually it becomes icon-first with the label revealed on hover/focus. Pinned libraries appear below the system items. Edit mode and `profileDeviceSettingsService.saveNavPrefs` keep working as-is — only visuals change.

**Topbar** is a new `Glass` element with three slots:

- Left: logo + active library/server switcher (replaces today's `ServerManagerModal` trigger).
- Center: universal search input (replaces today's right-side `SearchBar`). Pressing `/` focuses it from anywhere.
- Right: profile chip → opens the existing `MainLayoutUserMenu` dropdown (no behavior change).

The notification bell from admin **does not** appear here — the client has no admin notifications.

## Routes and page inventory

This redesign does not change route paths to avoid breaking deep links and bookmarks. Pages are reorganized inside, not relocated.

| Route | Component | What changes |
| --- | --- | --- |
| `/` | `pages/Client/HomePage.tsx` | Full cinematic redesign with Hero + dynamic rails. |
| `/library/:id` | `pages/Client/LibraryPage.tsx` | Hero strip, filter chips, posters grid, floating LetterRail. The Library/Collections/Recommendations tabs become the new `Tabs` primitive. |
| `/discovery` | `pages/Client/Discovery/DiscoveryPage.tsx` | Rail-based, larger spotlight rail at top. Provider chips per item. |
| `/discovery/details/:id` | `DiscoveryDetailsPage.tsx` | Mirrors new `MediaDetailsPage` layout. |
| `/discovery/actor/:id` | `DiscoveryActorPage.tsx` | Actor hero + filmography rails. |
| `/discovery/view-all/:rowId` | `DiscoveryViewAllPage.tsx` | Full grid view of one rail. |
| `/media/:id` | `pages/Client/Media/MediaDetailsPage.tsx` | Full-bleed cinematic backdrop, big Play CTA, tabs for Overview/Episodes/Cast/Extras/Similar. |
| `/actor/:id` | `pages/Client/Media/ActorDetailsPage.tsx` | Cinematic hero + filmography rails. |
| `/live-tv` | `pages/Client/LiveTv/LiveTvPage.tsx` | New "On Now" hero at top + simplified channel grid. |
| `/live-tv/guide` | `pages/Client/LiveTv/LiveTvGuide.tsx` | Refined EPG with cleaner timeline, better contrast, channel logos in roundels. |
| `/live-tv/dvr` | `pages/Client/LiveTv/DvrDashboard.tsx` | Tabs primitive; series folders use `MediaStill`. |
| `/audio` | `pages/Client/Audio/AudioHubPage.tsx` | Hero spotlight + Music / Radio / Podcasts tabs. |
| `/playlists` | `pages/Client/Playlists/PlaylistsPage.tsx` | Tabs primitive; Daily Mixes get the new gradient `MediaStill` treatment. |
| `/playlists/:id` | `PlaylistDetailsPage.tsx` | New track-list layout with sticky play CTA. |
| `/playlists/smart/:id` | `SmartPlaylistDetailsPage.tsx` | Same shell with a rule-tree badge. |
| `/collections` | `pages/Client/Collections/CollectionsPage.tsx` | Tabs across libraries kept; grid uses `MediaPoster`. |
| `/collections/:id` | `CollectionDetailsPage.tsx` | Cinematic backdrop from collection hero artwork. |
| `/watchlist` | `pages/Client/WatchlistPage.tsx` | `PageHeader` + posters grid + `EmptyState`. |
| `/calendar` | `pages/Client/CalendarPage.tsx` | Same calendar shell, restyled with template tokens. Events get type chips. |
| `/search` | `pages/Client/SearchPage.tsx` | Sectioned results with `MediaRail` per type. Actors keep circular `MediaPoster` variant. |
| `/recommendations` | `pages/Client/RecommendationsPage.tsx` | Single page of provider-grouped rails. |
| `/profile/history` | `pages/Client/ProfileHistoryPage.tsx` | Restyled with template tokens — minor pass. |
| `/settings` | `pages/Client/SettingsPage.tsx` | Major restructure — see below. |

## Settings restructure

The current Settings is a single long page with playback + provider toggles. New structure uses the `Tabs` primitive:

| Tab | Contents |
| --- | --- |
| **Templates** _(new)_ | Gallery of available templates with swatch strips. Active-schedule banner when a scheduled template is in effect. "Set as my default" / "Override for this schedule" actions. See `client-templates.md`. |
| **Playback** | Bitrate, resolution, audio channels, crossfade, EQ, captions defaults. Pulled out of today's flat list and grouped under sub-headers. |
| **Providers** | Live TV / Radio provider toggles + hidden channel lists. |
| **Account** | Links to `AccountSettingsPage` and `ProfileHistoryPage` (kept as separate pages, surfaced here for discoverability). |
| **Devices** | "Sign me out of other devices" + this device's capabilities readout. Mirrors what `AuthorizedDevicesPage` shows on the admin side, scoped to the current profile. |
| **About** | Server version, plugin list (read-only), open-source links. |

## What stays exactly where it is

- All authentication pages (`LoginPage`, `RegisterPage`, `SetupPage`) — only restyled to apply template tokens. Login canvas gets a new `loginCanvas` background slot in `ThemeManifest`.
- `ProfileSelectionPage` — restyled; profile cards get the cinematic backdrop treatment.
- `AccountSettingsPage` — same content, applies new primitives.

## Removed and merged

| Removed | Reason | Replacement |
| --- | --- | --- |
| Hand-rolled tab bars across `LibraryPage`, `AudioHubPage`, `CollectionsPage`, `PlaylistsPage`, `LiveTvPage` | Inconsistency | `Tabs` primitive |
| Hand-rolled empty states | Inconsistency | `EmptyState` primitive |
| Raw `<select>` for video tracks | Doesn't theme, bad UX | `QualityPanel` slide-in |
| `HomeCustomizeModal` / `DiscoveryCustomizeModal` keep their behavior but move under a unified "Customize this page" action on the new `PageHeader` | Discoverability | Same modals, surfaced consistently |
| Three player wrappers (`GlobalVideoPlayer`, `LiveTvPlayer`, `LiveRadioPlayer`) rendered conditionally in `MainLayout` | Duplication | New `PlayerSurfaceProvider` that picks the right surface based on `playbackContextType` from `PlayerContext`. Each surface still exists; the chrome is unified. |
| Hardcoded `bg-[#1e1f22]` chains across pages | Hardcoded color | `vora-card` + `var(--vora-*)` |

## Nav data file

Mirroring `components/Admin/Shell/adminNavData.tsx`, create `components/Client/Shell/clientNavData.tsx` with:

- `CLIENT_NAV` — `{ label, pathTemplate, icon, section, requires?: FeatureGate, libraryMediaType? }`.
- `Icons` — the SVG-path map for client icons.
- `resolveClientPath(template, serverId?)` — the `/library/:id` style rewriter.

Both `MainLayoutSidebar` and the new universal search (which is the client's Cmd-K equivalent) consume this list. Add a client page once, it appears in both nav and search.

## Universal search

The center-topbar search is more than today's `SearchBar`. It's a true command palette:

- Press `/` or `Cmd+K` to focus from anywhere.
- Typing routes through `searchService.searchAllServers(query)` for media + a local fuzzy-match against `CLIENT_NAV` for pages.
- Result groups: Media (movies, TV, music), People, Pages, Discovery (external).
- Enter opens the result; arrow keys navigate.

This replaces `SearchPage` as the **primary** search surface. `SearchPage` remains for the "see all results" flow when a user wants the full sectioned page.
