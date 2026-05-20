# Page-by-page redesigns

Each section: **role** (what the user does), **layout** (top → bottom), **interactions**, **data** (which existing services it calls — no API changes unless noted).

## Home (`HomePage.tsx`)

**Role.** Welcome and "what should I watch right now."

**Layout.**

1. **Hero** (50vh, `CinematicBackdrop intensity="hero"`). Auto-cycles every 8s through 3–5 featured items. Title (display weight), one-line synopsis, two CTAs: `Play` (primary, accent fill) and `More info` (secondary, glass). A row of progress dots indicates cycle position. Pulls from a new "Spotlight" smart list that the admin or auto-recommender can populate (no schema change — the home layout config can flag one list as `spotlight`).
2. **Continue Watching** rail (`MediaStill` for episodes, `MediaPoster` for movies). Progress bar overlay. Resume / Mark watched on context menu.
3. **Dynamic rails** as configured in `profileDeviceSettingsService.getHomeLayout()`. Each smart list is one `MediaRail`. Order is user-customizable via the existing `HomeCustomizeModal` (now triggered from `PageHeader`'s action slot).

**Interactions.** Arrow keys navigate cards. `Enter` plays movies / opens details for shows. `M` toggles mark-as-watched on focused card.

**Data.** `syncService.getContinueWatching()`, `smartListService.getActiveLists()`, `profileDeviceSettingsService.getHomeLayout()`. Spotlight items resolve via the admin smart-list flagged `spotlight` — falls back to top-recommended items if none flagged.

## Library (`LibraryPage.tsx`)

**Role.** Browse a single library's media.

**Layout.**

1. **Library hero strip** (28vh). Collage of 5 randomly-rotated artworks from the library, layered with the gradient mask. Library name + stats (`12,341 movies · 4.2TB · scanned 2h ago`).
2. **Filter chips** (`Chip` primitive). Genre, decade, rating, watched/unwatched, resolution. Selected chips animate to `accent-soft`. Sort dropdown on the right.
3. **Posters grid** (`MediaPoster` grid, density-responsive — 6 / 7 / 8 cols depending on viewport and `density` flag). Hover reveals title + meta. Right-edge `LetterRail` for A–Z jump.
4. **Tabs** ("All", "Collections", "Recommendations") via `Tabs` primitive. "All" is the default; the other two surface today's `collectionService.getLibraryCollections()` and `recommendationService` rails inside the same page (so the user doesn't lose the library hero).

**Admin actions** stay where they are (3-dot dropdown next to header) but use a themed `Menu` instead of inline absolute-positioned div.

**Data.** Unchanged.

## Discovery (`DiscoveryPage.tsx`)

**Role.** Browse what's out there (external providers).

**Layout.**

1. **Featured Discovery row** at top — wider `MediaPoster` variant, 4 per row, with provider badge (TMDb logo, etc.) in corner.
2. **Standard rails** from `discoveryService.getRowItems()` keyed by `discoveryService.getAdminConfigs()`.
3. **Customize layout** action moves to `PageHeader`.

Empty rails render `EmptyState` with "this provider has no items right now" instead of disappearing.

**Data.** Unchanged.

## Media Details (`MediaDetailsPage.tsx`)

**Role.** Decide whether and how to play something. This is the most important page in the entire client.

**Layout.**

1. **Backdrop** (70vh, `CinematicBackdrop intensity="detail" parallax`). Gradient mask fading to canvas over the lower 40%.
2. **Hero card**, floats over the lower portion of the backdrop:
   - Left: poster (large, `MediaPoster` variant `xl`).
   - Right: title (display), tagline (italic), badges (year · runtime · rating · resolution chips · codec chips), one-paragraph synopsis.
   - Below: primary `Play` button (accent fill, 56px tall, includes resume time if any). Secondary `Trailer`, `Add to playlist`, `Mark as watched`, `Edit` (admin only).
3. **Tabs**: Overview · Episodes (TV only) · Cast · Extras · Similar.
   - **Overview**: full synopsis, full cast list, crew, languages, directors.
   - **Episodes**: same `MediaEpisodesList` content but with the new `MediaStill` card style and a per-season chip switcher.
   - **Cast**: grid of circular `MediaPoster` variant `actor`. Each links to `ActorDetailsPage`.
   - **Extras**: `MediaStill` rail of trailers/featurettes.
   - **Similar**: `MediaRail` of recommendations.
4. **Stream details strip** at bottom: codec, file size, audio tracks, subtitle tracks. Replaces the in-page raw `<select>`s — these become the `QualityPanel` slide-in invoked from the player or a "Quality" button beside `Play`.

**Data.** Unchanged.

## Live TV (`LiveTvPage.tsx`)

**Role.** Watch what's on right now.

**Layout.**

1. **On Now hero** (40vh). Currently-tuned channel's poster/program backdrop on left, "what's on" panel on right with progress bar, "Up Next", and quick channel `Tabs` for favorites.
2. **All channels grid** below — channel logos in dark roundels, current program label under each.

The full EPG moves to `/live-tv/guide` (we already have it). The On Now page is the new "fast pick" surface.

**Data.** Unchanged.

## Live TV Guide (`LiveTvGuide.tsx`)

Same architecture (channels left, timeline right). Visual changes only:

- Cleaner timeline ticks (15-minute minor, 30-minute major).
- Channel logos in roundels (consistent diameter regardless of source aspect ratio).
- Program tiles use surface/raised tokens with accent stroke on recordings.
- Program hover popover becomes a `Glass` panel.
- Search and filters become `Chip` row.

**Data.** Unchanged.

## DVR Dashboard (`DvrDashboard.tsx`)

Tabs primitive for Completed / Upcoming / Failed. `DvrSessionCard` rebuilt as a `MediaStill` variant with stacked-effect for series folders. Series-vs-single cancel modal uses `useDialog` instead of custom modal.

**Data.** Unchanged.

## Audio Hub (`AudioHubPage.tsx`)

**Role.** Music, radio, podcasts.

**Layout.**

1. **Hero** (35vh) — when a track is playing, shows ambient artwork; otherwise surfaces "Made for you" mix art.
2. **Tabs**: Music · Radio · Podcasts.
3. **Music tab**: rails for Recently Played, Liked, Albums, Artists, Daily Mixes (gradient `MediaStill`).
4. **Radio tab**: existing 6-col grid of circular station logos; search + filters become `Chip` row.
5. **Podcasts tab**: rail of subscribed shows + grid of latest episodes.

The persistent `NowPlayingBar` (new primitive) sits at the bottom of the viewport across the whole client whenever music is playing.

**Data.** Unchanged.

## Playlists (`PlaylistsPage.tsx`)

Tabs primitive for All / Music / Video. Three sections inside All:

- Daily Mixes — `MediaStill` with gradient + optional overlay.
- Smart Playlists — `MediaPoster` variant with ⚙ corner badge.
- User Playlists — `MediaPoster`.

Create flow (manual vs smart) becomes a two-step dialog using `useDialog` extended with a custom dialog kind. **No new data flows.**

## Collections (`CollectionsPage.tsx` / `CollectionDetailsPage.tsx`)

CollectionsPage gets `Tabs` for library scoping. CollectionDetailsPage gains a cinematic backdrop computed from the collection's hero artwork (or composited from member posters when none).

## Watchlist (`WatchlistPage.tsx`)

Simple: `PageHeader` + `MediaPoster` grid + `EmptyState`. The current ad-hoc layout becomes a primitive composition.

## Calendar (`CalendarPage.tsx`)

Same calendar grid; events become `Chip` variants colored by type. Day cells use `bg-surface`. The legend becomes part of `PageHeader`'s action slot.

## Search (`SearchPage.tsx`)

Sectioned results in `MediaRail`s. Actors keep the circular `MediaPoster actor` variant. Discovery section labels external items with a "Discovery" chip. Universal-search palette in the topbar is the **primary** search surface; this page exists for the explicit "see all" flow.

## Settings (`SettingsPage.tsx`)

Tabbed shell (Templates, Playback, Providers, Account, Devices, About). All tabs use the `vora-card` surface + grouped form sections. Track/quality selects become a themed `Select` input variant. See `client-templates.md` for the Templates tab specifically.

## Player chrome

**Video player (`GlobalVideoPlayer.tsx` / `LiveTvPlayer.tsx`)**

Auto-hiding chrome (existing `useAutoHideControls` keeps working). Bottom bar restyled with `Glass`. Right-side `QualityPanel` slide-in for tracks, captions, and quality. New "Ambient" mode (default on) — derives a low-bitrate blurred extension of the video into the letterbox area for a more immersive feel. Toggleable in Settings → Playback.

Keyboard map (no change, codified):

| Key | Action |
| --- | --- |
| Space / K | Play / pause |
| J / L | Seek -10s / +10s |
| F | Fullscreen |
| M | Mute |
| ← / → | Seek -5s / +5s |
| ↑ / ↓ | Volume |
| C | Captions toggle |
| , / . | Frame step (paused) |

**Music Now Playing (`NowPlayingFullscreen.tsx`)**

Three-pane layout: Artwork (left, 40%), track info + scrubber + controls (center), Queue / Lyrics tabs (right). Background is a giant blurred version of the artwork with a 0.55 scrim. Crossfade on track change. Lyrics keep the existing LRC parser; line-level sync becomes a smooth interpolated highlight.

**Live Radio (`LiveRadioPlayer.tsx`)**

Compact `Glass` bar variant. Album art is replaced with the station logo. Bigger visualizer band as ambient motion.
