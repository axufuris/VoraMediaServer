# Client UI patterns (`Vora.Web`)

How the client pages are put together: tab-host pages, the media tile system, the player's shared primitives, remote-control (D-pad) navigation, and row hover. The folder layout, dialogs, and the rules every frontend change follows are in [`docs/frontend-conventions.md`](frontend-conventions.md).

## Tab-host pages and the `embedded` prop

Some client pages are hosted as a tab inside another page rather than living at
their own route. `HomePage` hosts **Home / For You / Collections / Watchlist / Playlists**.

**A discovery result that is already in the library opens the library page.** Discovery, search, calendar and watchlist results carry `mediaItemId` when the server recognised the title as one it holds; `utils/discoveryNavigation.ts` turns that into `/media/{id}` and falls back to the discovery route only when it is absent. Routing to discovery regardless shows a Play-less page for a film the viewer owns, so use the helper rather than building the path at the call site.

**Discover is no longer a tab host.** The Release Calendar was promoted to its own
route and nav destination, which left `DiscoverHubPage` wrapping one tab around one
page, so it is gone and `/discovery` renders `DiscoveryPage` directly. The calendar
lives at `/calendar` with a **`calendar`** nav id — chosen to match what the Android
client already writes, because the nav-prefs blob is shared between them and a
different id would silently fail to round-trip a pin or a reorder.

**For You is recommendations, and they come from your own library**, so it sits
on Home rather than in Discover — Discover is for titles you don't have. The
global endpoint (`/api/recommendations/global`) is the per-library one with a
null library id, so it already spans every library the profile can see; there is
nothing to group or aggregate on top of it. Each library page keeps its own
**For You** tab, which is the same computation narrowed to that library. Both are
gated on `FeatureGate.ForYou`, so the Home tab disappears with the feature rather
than rendering a page whose calls 403.

A hosted page drops its own `PageHeader` — the tab bar already names it — and
moves any page-level action into a right-aligned row above its content. Sub-tabs,
filters, URL params and data loading are unchanged.

A page that is hosted **and** still has its own route takes an `embedded?: boolean`
prop to switch between the two headers — `PlaylistsPage` and `WatchlistPage` do.
A page that is only ever hosted takes no prop and just renders the hosted form;
`CollectionsPage` has no standalone route, so it has no `embedded` prop and no
unreachable branch. Don't add the prop speculatively.

The host owns which tab is active and persists it in `sessionStorage`
(`home_active_tab`, `discover_active_tab`), so returning from a detail page lands
back on the tab you left. Put page-level controls in the `Tabs` `actions` slot
only when they belong to the host itself — Home's Customize button does; a tab's
own actions stay inside that tab.

**The watchlist is not a Discovery feature.** It holds library items as well as
titles that aren't in the library, so it is a Home tab, is shown unconditionally,
and its API lives at `/api/watchlist` rather than under `/api/discovery` — that
group is gated on the Discover feature and would 403 with Discover switched off.
An entry is keyed by its external provider identity whenever one is known, so a
title bookmarked from Discovery and the same title once it lands in the library
are one row; a library item with no external match is keyed by its own id.

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

**Actor pages.** The library actor route and the discovery actor route both
render `ActorProfile`. Credits are split into **On Server** and **Known For** by
whether the title is in the library, *not* by whether a local cast link exists —
a recurring player is often missing from the show-level cast a scan stored, so
splitting on the link stranded owned titles under "Known For" with an "In
library" badge. The library page unions its cast links with any provider credit
the library turns out to hold, so nothing is lost either way.

A person's biography and life dates are filled in by
`TriggerActorMetadataRefreshAsync`, which drains **50 actors per run** (those
with a null `Biography`), so a large library backfills over several scans. Until
a row is filled the page falls back to the provider's copy, which is why the
library actor page fetches discovery details when the Discover feature is on.

**Inherited facts.** A season carries no genres, production companies or cast of
its own — `ProcessTvSeasonsAsync` never writes them — and an episode only has
them when the provider returned episode-level credits. `MediaDetailsVM.Projection`
resolves each of genres, studios, directors and cast up the tree (own → season →
show) so those pages don't render a blank credits block. `Directors` is a
separate field rather than derived from `Cast`, because an episode often has a
guest cast with no directing credit: deriving would leave the director blank,
and falling the whole cast back would replace the guest actors with the show's.

**Detail pages.** `MediaDetailsPage` serves all four local types — Movie, TV Show, Season, Episode — so they share one hero by construction; `posterShape="still"` and the `S1 E2` title suffix are the only per-type differences. The local media details page and the discovery details page render the same `DetailHero`, so a title looks identical whether it's in the library or not. They differ only in what they pass to `actions`: local items get Play / Quality & tracks / Watched / more, discovery items get Add to Watchlist and nothing else. Everything a page has data for goes into a slot — `eyebrow`, `chips`, `ratings`, `notice` — and slots a page has no data for are simply omitted. `DetailHero` lays the backdrop *beside* the content and dissolves it at the left and bottom edges via `CinematicBackdrop`'s `mask="edge"`, so the artwork melts into the page instead of ending on a hard line. The backdrop keeps the artwork's 16:9 shape in the top right: 64% of the hero's width from `lg`, narrowing instead of cropping once it would pass 70% of the window height (`lg:w-[min(64%,calc(70vh*16/9))]`). A spacer in the content's grid cell holds the hero open to exactly that height (`lg:pt-[min(36%,70vh)]` — percentage padding resolves against the width), and the content centres against it. Stretching the box to the text's height instead made it short and wide on big windows, so the image was zoomed and its lower half cropped differently at every size. Poster and text sit side by side only from `lg` (the 16rem sidebar leaves a `md` window too little width for both; stills use a 16rem column until `xl`), and below that they stack under a full-width 16:9 backdrop. When the resolution/audio/subtitle chips sit in the corner (`lg`), the content keeps `lg:pb-16` free under the text so they never land on the overview.

**Sizing.** Card metrics are tokens in `styles/tokens.css`: `--vora-card-w-sm|md|lg`, `--vora-card-min-w`, `--vora-card-gap`, `--vora-card-title-size`, `--vora-card-caption-size`, `--vora-card-badge-size`, `--vora-person-w`, `--vora-video-w`, `--vora-row-title-size`, `--vora-row-gutter`. The widths are `clamp(rem, vw, rem)` so a tile follows both the root font size and the viewport instead of snapping at breakpoints. Change a token and every rail, grid and detail page moves together. Never size a tile in `px`.

## Player shared primitives

`components/Player/Controls/PlayerButtons.tsx` exports `PlayPauseButton`, `SkipButton`, `VolumeControl`, `FullscreenButton`, `MaximizeButton`, `CloseButton`. Both `GlobalVideoPlayer` and `LiveTvPlayer` use these.

`useAutoHideControls({ isMinimized, isPlaying, keepVisibleWhen })` and `useFullscreen(containerRef)` factor out the mousemove auto-hide effect and fullscreen toggle. Use them in any future player surface.

## Remote-control (D-pad) navigation

Browsers move focus on **Tab only** — there is no arrow-key spatial navigation to inherit. A TV remote's D-pad sends `ArrowUp/Down/Left/Right`, so on a screen with no handling the focus ring never moves: it stays on whatever it landed on and every OK press re-activates that one control. That is what made the music player's D-pad appear to "keep pressing play/pause".

`useSpatialNavigation(containerRef, enabled)` (`hooks/useSpatialNavigation.ts`) fixes that for a screen. The geometry lives apart from the DOM wiring in `utils/spatialNavigation.ts` so the interesting cases are testable.

Three rules worth knowing before reusing it:

- **Cross-axis distance is the gap between rects, not between centres.** A full-width seek bar overlaps every transport button below it, so from any of them it reads as directly above rather than far off to one side.
- **Movement is coned: a candidate further off the axis than along it is rejected.** Pressing Right at the end of a row therefore stops rather than leaping diagonally up to a header button. Stopping is the predictable behaviour on a remote.
- **Sliders keep the axis they scrub on.** `ownsDirection` lets a focused `input[type=range]` handle Left/Right itself while Up/Down still moves focus off it; text fields keep both axes so caret movement is not stolen.

A screen that opts in must also **give focus somewhere on open** — a D-pad can only move focus that already exists, and a remote cannot click to create it. `NowPlayingFullscreen` focuses play/pause.

The focus ring itself is global: `[data-vora-client] *:focus-visible` in `tokens.css`. A screen rendered outside that scope gets no visible ring, which makes the navigation invisible even when it works.

## Row hover

Clickable list rows (tracks, episodes, table rows) take `vora-row-interactive` from `tokens.css` rather than a Tailwind hover background. It fills the row with the template's accent — `--vora-accent-soft` with an `--vora-accent-soft-hover` border, the same pair the sidebar's selected item and the now-playing queue's current track use — and applies the same fill on `:focus-visible` for D-pad focus. Because both are template tokens, a template change recolours every row hover at once.

Buttons follow the same rule. `vora-icon-button` fills with `--vora-accent-soft` on hover and `:focus-visible`; `vora-pill` is a bordered pill that takes the accent fill and `--vora-accent-soft-hover` border when hovered, focused or `data-active="true"`. The now-playing screens use both, replacing the fixed `rgba(255,255,255,0.06)` pills and `hover:bg-white/5` buttons they had.

Hover fills in the client are the accent, not a neutral tint. Don't hover a row to `--vora-bg-sunken` (near-black in every dark template, so the row darkens into the page), to `hover:bg-white/5` (invisible on the light theme), or to any fixed colour that a template can't change.
