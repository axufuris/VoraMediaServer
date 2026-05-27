# Client primitive specs

The cinematic UI is composed from a small set of named primitives. The web client implements them in `Vora.Web/src/components/Client/Primitives/` (the reference implementation). Every native client must implement the same set with the same names, same prop shape, and same observable behavior. The look comes from [design tokens](design-tokens.md); the *contract* is in this file.

The ADR is [`docs/adr/0002-client-platform-strategy.md`](../adr/0002-client-platform-strategy.md). The web visual definition is [`docs/redesign/design-language.md`](../redesign/design-language.md) — read that first for the why.

## What "primitive" means here

A primitive is a single composable view with:

- A **stable name** (`Hero`, `MediaPoster`, `MediaRail`, …).
- A **prop contract** — the shape of inputs the primitive accepts. Same names across platforms; types translated naturally (e.g. `string | null` → `String?`).
- An **observable behavior** — what it does on interaction (focus, hover, press, scroll).
- A **motion budget** — which token motion values it respects.
- A **focus contract** — for TV, what focused/unfocused/focus-in/focus-out look like, and how D-pad traversal works within the primitive.

A primitive is *not* a leaf widget — it's a feature-level building block. Layout primitives like buttons, text, and icons are platform-native (`Button` in SwiftUI, `Button` in Compose, `<button>` on web).

## The set

The reference implementation is in `Vora.Web` today. The full list (matched 1:1 from `docs/redesign/design-language.md`):

`PageHeader`, `Hero`, `CinematicBackdrop`, `MediaPoster`, `MediaStill`, `MediaRail`, `Chip`, `Tabs`, `EmptyState`, `Glass`, `QualityPanel`, `NowPlayingBar`, `LetterRail`.

The specs below cover the high-leverage primitives — the ones a client can't function without. The others (`Chip`, `Tabs`, `EmptyState`, `LetterRail`) are simpler and their specs follow the same pattern: prop names from the web component, behavior from `design-language.md`, focus rules from the "TV focus rules" section below.

---

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

**TV focus:** Play CTA is focusable (the default), Info CTA is focusable. D-pad down from the Hero moves focus to the first `MediaRail` below. D-pad up from the first rail returns focus to the Hero's Play CTA.

---

### MediaPoster

2:3 portrait card representing a movie, TV show, album, playlist cover, or similar.

**Props:**
- `mediaItem: MediaItemVM` — id, title, poster URL, progress fraction (0–1) for "continue watching."
- `onActivate: () => void` — fire on click (web), tap (phone touch), or D-pad center (TV).
- `onFocusChange: (focused: boolean) => void | null` — optional, fires on focus enter/exit. Web ignores hover-as-focus; native TV uses focus events directly.
- `size: 'sm' | 'md' | 'lg' = 'md'` — visual size variant. `md` is the default rail size.
- `showTitle: boolean = true` — whether to render the title below the poster.

**Behavior:**
- Image renders at 2:3 aspect ratio, lazy-loaded, with a low-contrast skeleton placeholder using `misc.skeletonShimmer` while loading.
- Title (if shown) below the poster, single line, ellipsized at width.
- If `mediaItem.progress > 0`, a progress bar overlays the bottom of the poster — height 3px (web/phone) or 4px (TV), color `accent500`, full-width track at `accentSoft`.

**Motion:**
- On hover (web/phone with cursor): scale to 1.035, `translateY(-2px)`, accent-soft glow (`box-shadow: 0 0 0 2px accentSoft`). Duration `durationMed`, ease `easeOut`.
- On focus (TV): same scale and glow, but the glow is stronger (`0 0 0 3px accent500`). Title typography brightens from `textSecondary` to `textPrimary`. Duration `durationFast`.

**TV focus:** focusable. Activation = D-pad center. Receives focus from `MediaRail`'s sibling navigation (left/right). On focus enter, optionally calls `onFocusChange(true)` so the parent can update a "currently focused" preview elsewhere (e.g. Home's spotlighted backdrop).

---

### MediaStill

16:9 landscape card. Same prop shape as `MediaPoster`. Used for episodes, recordings, Live TV programs, playlist covers. All behavior matches `MediaPoster` except aspect ratio is 16:9.

The web/Apple/Android implementations should share enough of the body that they're nearly identical to `MediaPoster` — different aspect ratio, different rail sizing, same focus / motion / progress rules.

---

### MediaRail

Horizontal-scroll rail of `MediaPoster` or `MediaStill` cards. The primary content unit on Home and Library.

**Props:**
- `title: string` — section title (e.g. "Continue Watching", "New Releases").
- `items: MediaItemVM[]` — the cards to render.
- `cardKind: 'poster' | 'still' = 'poster'` — which child primitive to use.
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

---

### Glass

A frosted-surface wrapper used by the topbar, the player chrome panel, and popovers. Pure styling primitive.

**Props:**
- `children: ReactNode` (web) / `@ViewBuilder content` (SwiftUI) / `content: @Composable () -> Unit` (Compose).
- `intensity: 'subtle' | 'strong' = 'subtle'` — controls blur radius and opacity.

**Behavior:**
- Background `colors.bgGlass` (defined in the manifest's optional values, or defaults to `rgba(20,20,28,0.55)`).
- Backdrop blur 12px (`subtle`) or 18px (`strong`). On platforms without backdrop blur (older Android), fall back to a flat `bgGlass` color.

**TV focus:** not focusable — composes around content that may be focusable.

---

### QualityPanel

Slide-in right panel for video track / quality / subtitle / audio selection. Replaces raw `<select>`s in the player.

**Props:**
- `isOpen: boolean`
- `onClose: () => void`
- `sections: QualityPanelSection[]` — see below.
- `defaultFocusSectionId: string | null = null` — TV only; controls which section receives focus when the panel opens.

`QualityPanelSection` (same shape across platforms):
```
{ id: string, title: string, options: { id: string, label: string, selected: boolean, onSelect: () => void }[] }
```

**Behavior:**
- Slides in from the right edge over `durationMed`.
- Sections stacked vertically, each with a title and a list of options.
- Selected option marked with `accent500` and a checkmark icon.
- Closing dismisses with reverse slide.

**TV focus contract:**
- On open, focus moves to either `defaultFocusSectionId`'s first option, or the section that contains the currently-selected option.
- D-pad up/down moves between options within the same section.
- D-pad left/right (or page-up/down on remote where available) moves between sections.
- D-pad B / back closes the panel.

---

### NowPlayingBar

Persistent bottom-of-screen audio bar shown when music is playing. Tappable to expand into a full-screen Now Playing view.

**Props:**
- `track: MusicTrackVM | null` — if null, the bar hides.
- `isPlaying: boolean`
- `onTogglePlay: () => void`
- `onSkipNext: () => void`
- `onSkipPrevious: () => void`
- `onOpenFull: () => void` — fires on tap (web/phone) or D-pad center (TV) on the bar surface.
- `progress: number` — 0–1 playback progress; drives a thin progress line at the top edge of the bar.

**Behavior:**
- Slides up from the bottom edge when `track` becomes non-null. Hides with reverse slide when nulled.
- Bar height: 64px (web/phone) / 96px (TV, larger touch+focus targets).
- Album artwork on the left (square), title + artist in the middle (truncated), play/skip controls on the right.

**TV focus contract:**
- On TV, the bar is a focusable container with three focusable children: previous, play/pause, next. D-pad left/right traverses them. D-pad up exits the bar (focus moves to whatever was focused before). D-pad center on the bar's surface (when no child has focus) fires `onOpenFull`.

---

## TV focus rules (cross-cutting)

Every native client follows the same conventions so user behavior is consistent across Apple TV and Android TV.

**Default focus targets per page** (set on mount):
- Home → Hero's Play CTA.
- Library → first card in the first rail.
- Media Detail → primary CTA (Play / Resume).
- Live TV → currently-airing program in the EPG grid.
- Settings → first list item.

**Focus visuals:**
- Cards (`MediaPoster`, `MediaStill`): scale `1.035`, accent glow `0 0 0 3px accent500`.
- Buttons (any kind): elevation lift + glow with `accent500`.
- List items: leading accent bar (`4px wide accent500`), background tint `accentSoft`.
- Inputs: thicker border `accent500` + outer halo `accentFocusRing`.

**Focus motion:** duration `durationFast`, ease `easeOut`. No focus animation longer than 150ms — long focus animations make navigation feel sluggish.

**Focus memory:** every container that holds focusable children must remember the last-focused child when focus leaves, and restore it when focus returns. SwiftUI does this automatically with `@FocusState` per container; Compose for TV requires `Modifier.focusRestorer()` explicitly. Don't skip this — it's the single biggest difference between a TV app that feels good and one that doesn't.

**Forbidden patterns on TV:**
- Hover-only interactions (no cursor on TV remotes).
- Free-scrolling content with no focusable anchor.
- Multi-step gestures (long-press, swipe). All actions must be reachable via D-pad + center + back.
- Auto-rotating carousels without a pause on focus.

## Per-platform implementation notes

### Web reference

The reference implementations live under `Vora.Web/src/components/Client/Primitives/`. When evolving a primitive's contract, change the web implementation first, then update this doc, then update the native implementations. The web is the visual ground truth.

### SwiftUI (iOS / tvOS)

- Each primitive is a `View` struct in `VoraCore` Swift package's `Primitives/` group.
- Prop names match the web ones, translated to Swift conventions where appropriate (`onActivate` → `onActivate: () -> Void`).
- Focus contract uses `@FocusState` + `.focusable(true)` + `.focusSection()` per the rules above.
- Motion uses `withAnimation(.timingCurve(0.16, 1, 0.3, 1, duration: 0.24)) { ... }` to mirror `easeOut` + `durationMed`.

### Compose (Android phone / TV)

- Each primitive is a `@Composable fun` in `:core` module's `com.vora.primitives` package.
- Prop names match the web ones, translated to Kotlin conventions (`onActivate: () -> Unit`).
- Focus contract uses `Modifier.focusable()` + `Modifier.focusRestorer()` + `tv-foundation` primitives where the form factor is TV.
- Motion uses `AnimationSpec` derived from the emitted token durations.

### Behavior parity tests

When a primitive ships on a new platform, the test plan is the **same on every platform**:

1. Render the primitive in isolation with three sample item counts: 0 (empty), 1 (single), 10 (typical), 100 (overflow).
2. Drive every prop variation through screenshot tests if available, otherwise manual smoke tests.
3. On TV: validate every focus rule above — default focus, focus memory, exit behavior, focus visuals.
4. Validate motion respects reduced-motion settings.

The parity contract is *behavior*, not pixel identity. A 1px difference in shadow is fine. A different scrim color or a different focus traversal order is not.
