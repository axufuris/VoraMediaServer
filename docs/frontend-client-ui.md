# Client UI patterns (`Vora.Web`)

How the client pages are put together: tab-host pages, the media tile system, the player's shared primitives, remote-control (D-pad) navigation, row hover, and the scroll position kept across Back and Forward. The folder layout, dialogs, and the rules every frontend change follows are in [`docs/frontend-conventions.md`](frontend-conventions.md).

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

**Collections follows the sidebar.** `MainLayout` hands its sidebar entries to the pages through `SidebarItemsContext` (`layouts/sidebarItems.ts`, `loaded` once the profile's arrangement is in). A library the profile unpinned gets no Collections tab and its collections are never fetched, even from an old `?tab=` link (`isLibraryUnpinned`). Music libraries have no entries of their own, so they follow the one Music entry, which carries no server name, and share one **Music** tab (`?tab=music`; a link to one music library opens it): each library's collections are fetched and put back in the server's order (sort title, then title). Creating a collection there asks which music library it belongs to.

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

**Detail pages.** `MediaDetailsPage` serves all four local types — Movie, TV Show, Season, Episode — so they share one hero by construction; `posterShape="still"` and the `S1 E2` title suffix are the only per-type differences. The local media details page and the discovery details page render the same `DetailHero`, so a title looks identical whether it's in the library or not. They differ only in what they pass to `actions`: local items get Play / Watched / more, discovery items get Add to Watchlist and nothing else. Everything a page has data for goes into a slot — `eyebrow`, `chips`, `ratings`, `notice` — and slots a page has no data for are simply omitted. `DetailHero` lays the backdrop *beside* the content and dissolves it at the left and bottom edges via `CinematicBackdrop`'s `mask="edge"`, so the artwork melts into the page instead of ending on a hard line. The backdrop keeps the artwork's 16:9 shape in the top right: 64% of the hero's width from `lg`, narrowing instead of cropping once it would pass 60% of the window height or 40rem (`lg:w-[min(64%,calc(60vh*16/9),calc(40rem*16/9))]`). A spacer in the content's grid cell holds the hero open to exactly that height (`lg:pt-[min(36%,60vh,40rem)]` — percentage padding resolves against the width), and the content centres against it. The 40rem cap keeps a 1440p or ultrawide window from growing the hero to most of the screen; the backdrop is never taller than 640 px. Stretching the box to the text's height instead made it short and wide on big windows, so the image was zoomed and its lower half cropped differently at every size. Poster and text sit side by side only from `lg` (the 16rem sidebar leaves a `md` window too little width for both; stills use a 16rem column until `xl`), and below that they stack under a full-width 16:9 backdrop. When the resolution/audio/subtitle chips sit in the corner (`lg`), the content keeps `lg:pb-16` free under the text so they never land on the overview. On a local title those chips are menus (`HeroChoiceChip`, built by `TrackChoiceChips` from `useTitleTrackChoices`): **Automatic** plus each version or track, a pick remembered for the title on this device. A chip with only one option stays a plain `HeroChip`. The rules are in `docs/track-selection.md`.

**The Next episode card** on a show, season or episode page reads `upcomingEpisodesJson`: the provider's upcoming episodes as of the show's last metadata refresh. The server leaves out every episode the library holds before it sends the list (`UpcomingEpisodes.WithoutHeld` in `MediaManager`, ranges included), so a stale entry the library already has never shows, and the card names the first one still to come. It picks the soonest yet to air, else one that aired but isn't in the library ("Aired …"), and is hidden when nothing is left. The TV apps read the same filtered list.

**Season pages follow one episode, as the TV apps do.** The episodes are a row of stills (`EpisodeRail`, built from `MediaRow` + `MediaCard`) above Cast & Crew, and the hero is the **selected** episode's page: its title and `S1 E3`, facts, ratings, overview (clamped to three lines with `overviewLines`, falling back to the season's and then the show's, `TvShowOverview`), its file chips and track picks, and Play / Resume, Start over, Watched and Rate act on it. The hero centres its content, so it is `steady`: the room for the file chips (`lg:pb-16`) and a ratings row is held while the episode's details load, and the credits stay the season's, or every pick made the page jump. **Cast & Crew** below the episodes is the selected episode's (its own credits, else the season's and the show's, as `MediaDetailsVM.Projection` resolves them), and the season's until the episode's details load, as on the TV apps. `MediaCard`'s artwork box is `isolate`, so its badges and play button stack inside the card and never above a menu opened from the hero. The selection is `?episode=<id>` in the address (`useSeasonEpisodeSelection`), else the season's up-next episode (the server's pick in the profile's up-next order, below: `FirstEpisodeToPlayAsync`), else the first; its full details load 150 ms after it is picked. A click on a tile selects it, and so do focus and Left / Right, so a keyboard steps along the row like a remote; the round play button on the tile (`MediaCard`'s `onPlay`, shown only while that tile is hovered or the button itself has keyboard focus, never by a media query about the device, which lit every tile's button on computers whose browser reports a touch screen) plays it from where it was left, tracks left to the server as for Up Next. **Not hover:** reaching Play would pass over other tiles and change the hero under the pointer, and touch has none. The hero's menu holds the episode's entries, then the season's under a heading (`menuHeading`): Mark the season watched, Rate the season, its own menu. **Season pills** at the top (`SeasonPills`, in `DetailHero`'s `topContent`) switch season, replacing the address, after a **Show** pill that opens the show; the row is there on every season page once its seasons load, a one-season show's too, as the way back to the show. **An episode has no page:** `/media/<episode>` moves to its season with the episode selected, and links go there directly through `mediaPath` (Continue Watching carries `SeasonId`). Search returns no episodes, on the web or Android. Play resolves a season id to the same episode on the server (`ResolvePlayableMediaIdAsync`), and the button says what it will do (`playButtonFor`): **Resume S1 E2 20:50** or **Play S1 E3**, **Play next** on a show. A show with **one season** shows that season's row in place of a Seasons row of one, where a click opens the episode on its season; the show page stays for its menu (Fix match, Edit metadata), trailers, rating and watchlist.

**A profile can pick a list instead of the row** (`UserProfile.EpisodeLayout`, Settings → Account → Playback preferences; the row is the default). `EpisodeList` lays the episodes down the page as they were before the row: a `MediaCard` still, then the number and title, date, length, the profile's rating, the admin's, and three lines of plot. The still's round button plays the episode from where it was left. The title and plot pick it, as a click on a tile does, and scroll back up to the hero; the picked one is highlighted (`aria-current`, the accent fill). On a one-season show's page they open it on its season. `useEpisodeLayout` reads the setting and the page draws neither until it answers, so a list profile never sees the row first; a server too old to know the setting gets the row.

**Where Play starts on a show or season** is the profile's `UpNextOrder`, in the same card. **In order** (the default) is the first episode not watched to the end, resumed if it is part way. **Where I left off** is the episode played most recently: resumed if part way, or the one after it if it was finished; with nothing played it falls back to in order. `EpisodeToPlay.PickAsync` decides for the season page's starting selection, the show and season Play (`GetUpNextAsync`, `ResolvePlayableMediaIdAsync`) and Continue Watching, so they always agree. Nothing counts as part way before **30 seconds** (`WatchStateTransition.MinimumResumeSeconds`): an earlier progress ping saves no resume point, so a peek at an episode gives it no Resume button, no Continue Watching tile and not the "left off" spot. The re-watch grace (`RestartGraceSeconds`) is the same 30 s, so a finished episode restarted and stopped before then stays finished.

**Sizing.** Card metrics are tokens in `styles/tokens.css`: `--vora-card-w-sm|md|lg`, `--vora-card-min-w`, `--vora-card-gap`, `--vora-card-title-size`, `--vora-card-caption-size`, `--vora-card-badge-size`, `--vora-person-w`, `--vora-video-w`, `--vora-row-title-size`, `--vora-row-gutter`. The widths are `clamp(rem, vw, rem)` so a tile follows both the root font size and the viewport instead of snapping at breakpoints. Change a token and every rail, grid and detail page moves together. Never size a tile in `px`.

## Library sort

A movie, TV or home video library opens on, in order:

1. **A sort picked on that library's page**, remembered on this device for this profile (`vora_library_sort_<profileId>_<libraryId>`), but only while the profile's default is the one it was picked under. Changing the default in Settings moves every library to it.
2. **The profile's library sort**, from Settings → Account → Library sort order (`UserProfile.LibrarySort` / `LibrarySortDescending`, `GET`/`PUT /api/users/profiles/me/library-sort`). It follows the profile to every device.
3. **The server default**: titles A to Z, home videos newest first (`serverDefaultSort`).

The keys, labels and these rules live in `utils/librarySort.ts`, shared by `LibraryPage` and `LibrarySortSection`. The page shows its skeleton until the profile's sort has loaded, so it never reorders after the posters appear; if the request fails it opens on the server default. The old per-device keys (`vora_library_sort_<libraryId>`, `vora_library_sortdir_<libraryId>`) are still read while the profile is on the server default, and are removed the next time a sort is picked.

## The title menu

Right-clicking a movie, show, season or episode, or holding it on a touch screen, opens the same menu as the title page's "more" button. One set of actions feeds both, so they cannot drift.

- **`useMediaItemActions(media, options)`** (`components/Media/ItemMenu/`) returns the menu's `entries` and the `dialogs` they open (media info, edit metadata, add to playlist / collection, fix match, marker editor), and owns their state. What it offers depends on the title's type and on whether the viewer is an admin. **An admin's maintenance actions sit behind one row**, "Manage movie / show / season / episode…" (`menuSubmenu`): fix match, clear or redo a match, remove metadata, refresh, scan, analyze, thumbnails, markers, delete. `ActionMenuList` swaps that row's entries in under a Back row without closing the menu (Right goes in, Left and Back come out), a list in place rather than a fly-out so a remote moves through it the same way. There is **no Go to season / Go to show**: a tile opens its season, and a season page's Show pill opens the show. A menu holding two titles' entries (a season page's episode, then the season) keeps their keys apart with `withKeyPrefix`, or the season's Manage row opened the episode's. `MediaDetailsPage` draws the rows with `ActionMenuList`; its version, audio and subtitle picks are menus on the hero chips instead.
- **`MediaItemMenuProvider`** wraps `MainLayout`. It is the one host for the menu on posters: it loads the title with `mediaService.getMediaItem`, shows `ActionMenu`, and keeps the dialogs alive after the menu closes.
- **A poster only says which title it is.** `MediaCard` takes `menu={mediaMenuTarget(item, serverId, { onChanged })}`. `mediaMenuTarget` returns nothing for anything that is not a Movie, TvShow, Season or Episode, so tracks, albums, channels and discovery results keep the browser's own menu. Pass `onChanged` (and `onDeleted` if it differs) when the page has a reload to run.
- **The poster menu leads with Mark as watched / unwatched and Add to / Remove from watchlist** (`withWatchActions`, set by the provider). The title page shows those two as buttons beside its menu, so its menu leaves them out. Both places run the same code, `toggleWatched` and `toggleWatchlist` in `ItemMenu/watchActions.ts`: marking a show or a season asks first and says how many episodes it changes, only the ones left to watch (`changedEpisodeCount` in `utils/watchState.ts`: a season by each episode's state, a show by its `unplayedItemCount`, else every episode), as Android and the Roku do. The watchlist is asked about every time the menu loads the title, because it can change on the title page between two openings.
- **`onChanged` matters more now that the menu changes what a tile shows.** Without it, a title marked watched keeps its old tick until the page is left. A page whose list is costly to load again (recommendation rows can cost a model call) takes the title `onChanged` is given and patches that one tile: see `RecommendationRow`.
- **A row that is not a `MediaCard`** (the episode list, playlist rows) uses `<MediaMenuArea target={...}>` in place of its outer `div`, or spreads `useMediaMenuTrigger(target)` itself.
- **Every new place a title is drawn passes a target.** That is the whole cost of supporting the menu there.
- **No delete button on a title's poster.** The menu already has **Delete item** for admins, so library posters don't pass `MediaCard`'s `onDelete`. It stays for tiles with no title menu: playlists and music mixes.
- **`ActionMenu`** (`Primitives/`) opens beside the pointer on a desktop, kept inside the window, and as a sheet from the bottom edge below 48rem. It sits at `z-[150]`: over the header, under modals.
- **Long press:** 500 ms without moving more than 10 px (`useMediaMenuTrigger`), so scrolling a row never opens it. Lifting that finger still produces a click, so the trigger swallows the next click on the poster, and `ActionMenu` ignores any click that did not start with a press on the menu itself. Without the second, the lift landed on the sheet that had just appeared and closed it, or picked a row.
- Actions go to the server the title is on (`target.serverId`), not the one in the address bar: a search result can belong to another server.

## Video playback: one controller, two engines

`components/Player/usePlaybackController.ts` holds everything the video player decides: markers and per-profile auto-skip (once per marker), commercial skip, the details and Up Next fetches, the Up Next hand-off, version, audio and subtitle picks (remembered per title), subtitle sideloading, scrub thumbnails and what the quality chips say. It drives an engine (`engine/PlaybackEngine.ts`) that only plays:

- `WebVideoEngine` is the `<video>` + `loadHls` code. It never switches tracks inside the file (`selectLocalTracks` is false), so an audio or version change restarts the session as before. It emits one `error` per stream, `beforeFirstFrame` until the `<video>` fired `playing`: from the element's `error` event for a file or Safari's native HLS it plays itself, and from hls.js for a fatal network error with a status (a 410 still means the end), for no answer after one `startLoad()` retry, and for anything hls.js gives up on after its one `recoverMediaError()`.
- `DesktopMpvEngine` forwards to `window.voraDesktop.player` inside Vora Desktop (`docs/clients/desktop/`). In a Direct Play session it switches audio and embedded subtitles, image ones too, inside the file by their `embeddedPosition`; a sidecar subtitle goes to mpv as VTT text.

`PlayerContext` keeps the session: the 10 s pings, close, `changeStreams`, play, pause and seek. In a browser it drives the `<video>` itself, as it always has (music shares that element). An engine that isn't the `<video>` attaches itself as a `PlaybackTransport` (`usePlaybackTransportHost`); from then on those calls go to it and its time and pause events drive the clock.

`GlobalVideoPlayer` is the web chrome over `usePlaybackController(webEngine)`. Its `useStreamRecovery` asks for a stream again when it dies after playing, as the Android and Roku players do: the play and HLS URLs are signed for 4 h (`StreamManager.PlayTokenTtl`), so after a long pause the next request is a 404. A stream that played restarts (`restartStream(position)`, a new session) at the last position seen while it was healthy, at once if it was playing and on the `<video>`'s next `play` if it was paused; at most once a minute (`recoversStream`). Anything else shows "This video couldn't play" or "This video stopped playing" and closes the player. `MainLayout` mounts `DesktopPlaybackHost` in its place when `playsVideoInMpv` (the app, mpv found, **Use the Vora player** on), decided once per session so a setting changed mid-video never swaps players. The host shows a small bar, sends mpv a `PlayerChromeState` (`desktopChromeState.ts`) at most ten times a second and only when it changed, turns the chrome's `ChromeIntent`s into the controller calls the web buttons make, and starts the next episode through `useUpNextAutoPlay`, the hook `UpNextOverlay` uses, so both wait the same 10 seconds after Up next appears (`playerPromptTiming.ts`, `docs/analysis-and-markers.md` → Player UX). When mpv fails it asks whether to carry on in the built-in player, and restarts the session there with the browser profile (`forceBuiltInPlayer`, until the player closes).

## Player shared primitives

`components/Player/Controls/PlayerButtons.tsx` exports `PlayPauseButton`, `SkipButton`, `VolumeControl`, `FullscreenButton`, `MaximizeButton`, `CloseButton`. Both `GlobalVideoPlayer` and `LiveTvPlayer` use these.

The volume lives in `PlayerContext`, not in the media element: `setVolume` keeps the level even when no element exists, and every new element gets it when it appears (a new item, or a new session re-keying the video player's `<video>`). Closing a player unmounts its element, and the next one used to start at the browser's full volume under a slider still showing the old level (issue #141).

`VideoSeekBar` (`Controls/`) is the video seek bar, in the full player and the mini player. It works with a mouse, a pen or a finger (pointer events, `touch-action: none`): press, drag and release, with the fill and the scrub preview (`onPointAt` → `ScrubPreview`) following the pointer and **one seek on release**, because a seek outside what is transcoded restarts the transcode. A cancelled touch keeps the old position. It is a focusable `role="slider"`, and `usePlayerKeyboard` still gives it the arrow keys. The bars were click-only `div`s, so a drag did nothing (issue #160). The music player's bar is a native range input.

`useAutoHideControls({ isMinimized, isPlaying, keepVisibleWhen })` and `useFullscreen(containerRef)` factor out the auto-hide effect (a mouse move or a key press shows the controls) and fullscreen toggle. Use them in any future player surface.

`usePlayerKeyboard` (`Controls/`) gives a full-size player its keys: **Space** plays and pauses through the same `togglePlayPause` the button calls, Left and Right skip back and forward through `skipBackward` / `skipForward`, and Up and Down change the volume. `GlobalVideoPlayer` and `LiveTvPlayer` use it with plain arrows (Shift with an arrow does the same); Live TV passes `canSeek` only when it can timeshift, so there the arrows only change the volume. The audio screens get it through `NowPlayingShell`'s `playerKeys` with `plainArrows: false`: their plain arrows move focus for a TV remote (below), so only **Shift with an arrow** seeks or changes the volume there, and live radio, which passes no skips, only gets the volume (issue #162). Rules it keeps (pure parts in `utils/playerKeys.ts`, tested on their own): A mouse click on the picture also plays and pauses, with the same indicator (`usePictureClick`, on the video player's controls layer): it counts only when the press starts and ends on the picture itself, not on a control and not at the end of a seek-bar drag, and a tap on a touch screen is left to show and hide the controls.

- It takes the key from the page (`preventDefault`), so the page behind does not scroll. A key with Alt, Ctrl or Meta is left to the browser, and so is Shift+Space (scroll up).
- A focused button, link, checkbox, menu item or text field keeps Space (`spaceBelongsToTarget`): after clicking the play button, Space presses it again rather than toggling twice. A focused slider, the picture and the page give it to the player. Holding Space toggles once (`event.repeat`).
- A text field, a `<select>` or an editable region keeps its arrows (`isTypingTarget`). A focused slider does not: the volume slider would otherwise move by its own step.
- It stands down while the player is minimised or one of its panels is open, and whenever any `[aria-modal="true"]` element is on the page. A hand-built overlay therefore has to carry `aria-modal`, as `LiveTvRecordModal` does.
- The steps are the viewer's Settings → Playback → Keyboard picks, read from `playback_prefs` on every press (default 10 s and 5%).
- It returns what `PlayerKeyIndicator` shows in the middle of the picture for 0.9 s ("Paused" / "Playing" for Space). Presses in one direction add up while it shows (+30s after three Rights). Its `role="status"` region stays mounted so a screen reader hears each change.

## Remote-control (D-pad) navigation

Browsers move focus on **Tab only** — there is no arrow-key spatial navigation to inherit. A TV remote's D-pad sends `ArrowUp/Down/Left/Right`, so on a screen with no handling the focus ring never moves: it stays on whatever it landed on and every OK press re-activates that one control. That is what made the music player's D-pad appear to "keep pressing play/pause".

`useSpatialNavigation(containerRef, enabled)` (`hooks/useSpatialNavigation.ts`) fixes that for a screen. The geometry lives apart from the DOM wiring in `utils/spatialNavigation.ts` so the interesting cases are testable.

Three rules worth knowing before reusing it:

- **Cross-axis distance is the gap between rects, not between centres.** A full-width seek bar overlaps every transport button below it, so from any of them it reads as directly above rather than far off to one side.
- **Movement is coned: a candidate further off the axis than along it is rejected.** Pressing Right at the end of a row therefore stops rather than leaping diagonally up to a header button. Stopping is the predictable behaviour on a remote.
- **Shift with an arrow is not a D-pad press.** It is left to the player's keys (`usePlayerKeyboard` on the now-playing screens), so it seeks or changes the volume instead of moving focus.
- **Sliders keep the axis they scrub on.** `ownsDirection` lets a focused `input[type=range]` handle Left/Right itself while Up/Down still moves focus off it; text fields keep both axes so caret movement is not stolen.

A screen that opts in must also **give focus somewhere on open** — a D-pad can only move focus that already exists, and a remote cannot click to create it. `NowPlayingFullscreen` focuses play/pause.

The focus ring itself is global: `[data-vora-client] *:focus-visible` in `tokens.css`. A screen rendered outside that scope gets no visible ring, which makes the navigation invisible even when it works.

## Row hover

Clickable list rows (tracks, episodes, table rows) take `vora-row-interactive` from `tokens.css` rather than a Tailwind hover background. It fills the row with the template's accent — `--vora-accent-soft` with an `--vora-accent-soft-hover` border, the same pair the sidebar's selected item and the now-playing queue's current track use — and applies the same fill on `:focus-visible` for D-pad focus. Because both are template tokens, a template change recolours every row hover at once.

Buttons follow the same rule. `vora-icon-button` fills with `--vora-accent-soft` on hover and `:focus-visible`; `vora-pill` is a bordered pill that takes the accent fill and `--vora-accent-soft-hover` border when hovered, focused or `data-active="true"`. The now-playing screens use both, replacing the fixed `rgba(255,255,255,0.06)` pills and `hover:bg-white/5` buttons they had. `vora-button-secondary` takes the same fill and border on hover and `:focus-visible` (it used to only darken its border, so dialogs such as Edit markers looked dead under the pointer), and `vora-button-primary` turns `--vora-accent-hover` on both; neither changes while disabled.

Hover fills in the client are the accent, not a neutral tint. Don't hover a row to `--vora-bg-sunken` (near-black in every dark template, so the row darkens into the page), to `hover:bg-white/5` (invisible on the light theme), or to any fixed colour that a template can't change.

## Scroll position

The client scrolls inside `MainLayout`'s `<main>`, not the window, so the browser never restored a position: Back from a title to a long library landed at the top. `useScrollRestoration` (its callback ref is on `<main>`) remembers where each history entry was left, keyed on `location.key`:

- **Back and Forward** put it back once the page is tall enough to reach it, so a library that reloads its items behind a skeleton still returns to the same row. A wheel, touch, click or key press first leaves the page where the viewer put it; after 8 seconds it goes as far as the page reaches.
- **A new page** starts at the top. The one `<main>` outlives every page, so a title opened from far down a library used to open part-way down.
- **Only the query string changing** (a tab or filter in the URL) keeps the position.

Positions live in memory, so a reload forgets them. Sort and filter come back on their own: the sort as described under Library sort, the filter per library (`vora_library_filter_*`).
