# Media primitive specs

Specs for the media primitives — `CinematicBackdrop`, `Hero`, `MediaCard`, `DetailHero`, `PersonCard`, `VideoCard`, `MediaRow`. What a primitive is, the full set, the remaining specs, the cross-cutting TV focus rules, and the per-platform notes are in [`docs/clients/primitive-specs.md`](primitive-specs.md).

### CinematicBackdrop

A large background image, masked at the bottom by a gradient to canvas, used behind hero / detail-page header regions.

**Props:**
- `src: string | null` — image URL. When null, the primitive renders a flat canvas-colored region of the same size.
- `intensity: 'hero' | 'detail' | 'ambient'` — controls scrim strength. `hero` is brightest (~70% of artwork visible at top); `detail` is medium (~50%); `ambient` is darkest (~25%, used as a template canvas image).
- `parallax: boolean = false` — on web, scrolls at 0.4× page-scroll until masked off. On native, ignored on TV (no scroll) but honored on phones.
- `transitionKey: string` — when this prop changes, the primitive crossfades from the old `src` to the new one over `motion.durationSlow` (web) / 560ms (native). Use the media item id.

**Behavior:**
- Image renders at full container width, aspect-fill, anchored to top.
- A linear gradient mask fades from `colors.bgCanvas / 0` at the top to `colors.bgCanvas / 1` at the bottom over the lower 40%, regardless of intensity (intensity controls the *upper* scrim).
- On `transitionKey` change: old image fades out as new image fades in; never cuts.
- Respects reduced-motion preferences: crossfade still runs (opacity-only), parallax disabled.

**Motion:** `durationSlow` (560ms) for the crossfade, with the standard `easeOut` curve.

**TV focus:** not focusable — pure decoration.

---

### Hero

Full-bleed featured area used at the top of Home. Composes `CinematicBackdrop` + title + subtitle + primary CTA.

**Props:**
- `mediaItem: MediaItemVM` — the featured item (see `Vora.Application` for the VM shape).
- `onPlay: () => void` — invoked when the play CTA fires.
- `onInfo: () => void | null` — optional secondary CTA ("More Info"). Hidden when null.
- `autoCycleMs: number | null = null` — when set, rotates through a parent-supplied list (see below). When null, static.

**Behavior:**
- Renders `CinematicBackdrop` with `intensity="hero"` and `src` from `mediaItem.heroBackdropUrl`.
- Title typography from `typography.fontSans`, hero scale (48/56 web, scaled equivalents native).
- CTA renders as a primary action button. On TV, this is the **default focus target** when the Home page mounts.
- If `autoCycleMs` is set, the parent re-renders with a different `mediaItem` every N ms; the `CinematicBackdrop`'s `transitionKey` change triggers the crossfade.

**Motion:** Inherits backdrop crossfade. Title text fades in 100ms after the backdrop settles.

**TV focus:** Play CTA is focusable (the default), Info CTA is focusable. D-pad down from the Hero moves focus to the first `MediaRow` below. D-pad up from the first rail returns focus to the Hero's Play CTA.

---

### MediaCard

The single media tile. One primitive covers every aspect ratio rather than a separate card per shape.

**Props:**
- `mediaItem: MediaItemVM` — id, title, type, artwork URL, progress fraction (0–1) for "continue watching", played state, unplayed count.
- `onActivate: () => void` — fire on click (web), tap (phone touch), or D-pad center (TV).
- `onFocusChange: (focused: boolean) => void | null` — optional, fires on focus enter/exit. Web ignores hover-as-focus; native TV uses focus events directly.
- `shape: 'poster' | 'still' | 'square' | 'circle' = 'poster'` — 2:3, 16:9, 1:1, or 1:1 masked to a circle (people, music artists).
- `size: 'sm' | 'md' | 'lg' = 'md'` — width variant. `md` is the default rail size.
- `showCaption: boolean = true` — whether to render the caption block below the artwork.
- `imageFit: 'cover' | 'contain' = 'cover'` — `contain` insets the whole image on the card surface instead of cropping it. Used for channel and station logos.
- `uncachedImage: boolean = false` — load the artwork URL directly instead of through the server's resize cache (`/api/artwork/thumb`), which only fetches from known artwork hosts. Podcast feeds and IPTV playlists point at any host, so their art must load directly.

**Behavior:**
- Artwork renders at the shape's aspect ratio, lazy-loaded, with a low-contrast skeleton placeholder using `misc.skeletonShimmer` while loading, falling back to the branded placeholder if the source is missing or fails.
- The caption block below the artwork is **derived from `mediaItem.type`**, not passed in: a title line plus zero or more muted sub-lines. Movie → year · edition. TV show → year. Season → show / season label / year. Episode → show / episode title / `S1 · E2`. Album → artist / year · album. Collection → item count. Every platform must produce the same lines for the same item; the web implementation lives in `utils/posterCaption.ts` and is the reference. A client that captions TV shows with a season count rather than a year reads `numberOfSeasons`, which is carried by `LibraryItemVM`, `MediaItemVM`, `MediaDetailsVM`, and `CollectionDetailsLibraryItemVM` — so a show reports the same count in a collection as in a library grid. It is the live count of seasons the server holds (non-missing), not `TvShow.NumberOfSeasons`, which is the provider's total for the series. Other list DTOs (`UpNextItemVM`, `PlaylistItemVM`, `WatchlistItemVM`, discovery results) do not carry it.
- If `mediaItem.progress > 0`, a progress bar overlays the bottom of the artwork — height 3px (web/phone) or 4px (TV), color `accent500`, full-width track at `accentSoft`.
- Status affordances render in the artwork's corners: watchlist flag top-left, unplayed count or played check top-right, provider/quality badge bottom-left.
- **One top-right box on every tile**, inset 6px from the top and right edges: a 4px-radius chip on `bg-overlay` with a 1px `border-subtle`, 10px bold text, 2px × 6px padding. An episode's box holds its number (`E1`, `E1-E2`) with the accent check to its left once watched; any other watched tile gets the same box holding just the check (2px × 4px padding, 12px check). Never a circle. The unplayed count uses the same box shape on `accent-500`. Web: `WatchedBadge.tsx` (`PosterCorner`, `CornerChip`).

**Sizing:** widths are relative, not fixed pixels. Web reads the `--vora-card-w-*` tokens (`clamp(rem, vw, rem)`); native clients scale the equivalent values by the platform's dynamic-type / display-size setting.

**Motion:**
- On hover (web/phone with cursor): scale to 1.035, `translateY(-2px)`, accent-soft glow (`box-shadow: 0 0 0 2px accentSoft`). Duration `durationMed`, ease `easeOut`.
- On focus (TV): same scale and glow, but the glow is stronger (`0 0 0 3px accent500`). Title typography brightens from `textSecondary` to `textPrimary`. Duration `durationFast`.

**TV focus:** focusable. Activation = D-pad center. Receives focus from `MediaRow`'s sibling navigation (left/right). On focus enter, optionally calls `onFocusChange(true)` so the parent can update a "currently focused" preview elsewhere.

---

### DetailHero

The top block of a detail page. One primitive serves both an in-library title and an external (discovery) title — the difference is the actions, never the layout.

**Props:**
- `backdropSrc`, `posterSrc`, `title` — the artwork and the name.
- `posterShape: 'poster' | 'still' = 'poster'` — episodes use `still`.
- `eyebrow`, `titleSuffix`, `subtitle` — type/year, `S1 E2`, and the show name for a season or episode.
- `chips`, `ratings`, `credits`, `actions`, `notice`, `overview` — slots. A platform omits a slot it has no data for; it never substitutes a different layout. `credits` is the labelled director / genres / studio block.
- `onBack` — the back affordance always reads "Back". A detail page is reachable from several places (Discover, search, the Home watchlist), so a label naming one destination would be wrong from the others.

**Behavior:**
- The backdrop occupies the right of the header (about 64% of its width on wide layouts, full width when the layout stacks) and fades out at its **left and bottom** edges so it dissolves into the page rather than ending on a seam. It keeps the artwork's **16:9 shape** — never stretched to the content's height and cropped — capped at 70% of the screen height (where it narrows instead), and the header is at least as tall as the backdrop, with the content centred against it. Web does this with nested masks; native clients use the equivalent gradient mask.
- The poster sits left of the text column at a fixed relative width.
- `actions` is the only slot that differs by source: an in-library item gets Play / Start over / Add to watchlist / Rate / Play trailer / Mark watched / overflow (Quality & tracks and Media info live in the overflow); a discovery item gets Add to Watchlist alone. Playback controls must never appear for an item that isn't in the library, but **Add to watchlist appears on both** — the watchlist spans owned and unowned titles.

**TV focus:** the first action is the default focus target for the page. D-pad down leaves the hero for the first `MediaRow` below.

---

### PersonCard

Cast/crew tile: 4:5 portrait artwork, name, credited role, character name. Same focus and motion rules as `MediaCard`. `CastRow` composes it inside a `MediaRow`, sorting actors ahead of crew while preserving billing order within each group.

---

### VideoCard

16:9 trailer/extra tile with a centered play affordance and an optional uppercase type label in the top-left. Title below, clamped to two lines. Same focus and motion rules as `MediaCard`.

---

### MediaRow

Horizontal-scroll rail of `MediaCard`s. The primary content unit on Home, Discover and Library.

**Props:**
- `title: string` — section title (e.g. "Continue Watching", "New Releases").
- `items: MediaItemVM[]` — the cards to render.
- `cardShape: 'poster' | 'still' | 'square' | 'circle' = 'poster'` — passed through to the child cards.
- `onItemActivate: (item: MediaItemVM) => void` — fired by the child card.
- `onItemFocus: (item: MediaItemVM | null) => void | null` — fired on TV when focus enters / leaves a card. `null` means rail lost focus entirely.
- `peekIndicator: boolean = true` — on web, fades the right edge so users see there's more.

**Behavior:**
- Title above the row. Row scrolls horizontally with snap.
- On web: scroll with momentum, optional arrow buttons on the rail edges that paginate by one viewport. Edge gradient fade (right-fade always when overflow exists, left-fade only when scrolled past start).
- On phone (touch): identical to web; snap behavior is platform default.
- On TV: D-pad left/right traverses cards. **The rail does not scroll free-form** — focus drives scroll. When focused card approaches the right edge, the rail scroll-into-views the next card with a smooth scroll animation (`durationMed`).

**Motion:** Scroll-into-view uses `easeOut`, duration `durationMed`.

**TV focus contract:**
- The rail itself is a "focus group" — D-pad down moves focus out of the rail (to the next rail below). D-pad up moves to the previous rail (or to the Hero if this is the first rail under a Hero).
- D-pad left/right within the rail moves between cards. From the leftmost card, D-pad left moves focus *out* of the rail to the left (typically to the sidebar / nav).
- Focus memory: when the rail loses focus and regains it later, the last-focused card is refocused, not the first card. Use a focus restorer per rail keyed by `title`.
