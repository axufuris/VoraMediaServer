# Design language

The new client is **cinematic**: deep canvas, full-bleed artwork, generous typography, smooth motion. The feel should be closer to Apple TV+ / Disney+ than to Plex's current chrome. Templates extend this language with image-backed backgrounds and accent tints.

## Color model

Tokens stay on the existing `--vora-*` namespace defined in `styles/tokens.css`. The active template writes them at runtime via `ClientTemplateProvider`. The default client template (`vora-cinema`) ships these values.

### Surfaces (dark, layered)

| Token | Default | Role |
| --- | --- | --- |
| `--vora-bg-canvas` | `#08080b` | Page canvas behind everything |
| `--vora-bg-surface` | `#101015` | Top-level surface (cards, rails) |
| `--vora-bg-raised` | `#181820` | Modals, popovers, raised panels |
| `--vora-bg-sunken` | `#050507` | Player letterbox, hero scrim base |
| `--vora-bg-overlay` | `rgba(0,0,0,0.72)` | Modal backdrops |
| `--vora-bg-glass` | `rgba(20,20,28,0.55)` | Topbar / pill backgrounds (used with backdrop-filter blur) |

### Text

| Token | Default | Role |
| --- | --- | --- |
| `--vora-text-primary` | `#fafafa` | Headings, primary copy |
| `--vora-text-secondary` | `#c4c4cc` | Body, secondary metadata |
| `--vora-text-muted` | `#9090a0` | Tertiary metadata, hints |
| `--vora-text-disabled` | `#5b5b6a` | Disabled states |
| `--vora-text-inverse` | `#0a0a0e` | Text on light accent fills |

### Accents (template-overridable)

The default cinema template uses an amber accent — same family as today — but darker and richer. Each template overrides these freely.

| Token | Default | Role |
| --- | --- | --- |
| `--vora-accent-500` | `#f59e0b` | Primary CTAs, focus ring, active rail item |
| `--vora-accent-hover` | `#fbbf24` | Hover state |
| `--vora-accent-active` | `#d97706` | Pressed state |
| `--vora-accent-soft` | `rgba(245,158,11,0.16)` | Soft chip / hover wash |
| `--vora-accent-contrast` | `#1a1207` | Text on solid accent |

### Status colors

`success`, `warning`, `danger`, `info` keep their existing token shape but the defaults shift toward muted tones so they don't fight cinematic artwork.

## Typography

Single font family. We move from the current default to a tighter modern sans:

- Primary: `"Inter Tight", "Inter", system-ui, sans-serif`
- Mono: `"JetBrains Mono", ui-monospace, monospace`

Scale (vs admin which is denser):

| Size | Use |
| --- | --- |
| 48/56 — hero | Featured title on Home hero |
| 36/44 — display | Media title on detail pages |
| 24/32 — h1 | Page title (also on `PageHeader`) |
| 18/26 — h2 | Section / rail title |
| 15/22 — body | Default body |
| 13/18 — meta | Metadata, badges |
| 11/14 — caption | Disabled / micro |

Heading weight `600` (not 700 — the 700 grade looks heavy against artwork). Body `400`. No all-caps. Letter-spacing `-0.01em` on headings ≥24px.

## Density and layout flags

The existing `ThemeLayoutFlags` apply unchanged. Two flags matter most:

- `density: 'comfortable' | 'compact'` — comfortable is the cinematic default; compact reduces card sizes for users with 5K+ item libraries.
- `card: 'elevated' | 'flat' | 'outlined'` — elevated is default; flat suits high-contrast templates.

## Motion

All motion is **opt-in to a single duration scale** so it stays consistent across pages.

| Token | Default | Use |
| --- | --- | --- |
| `--vora-duration-fast` | `120ms` | Hover, focus, tooltip |
| `--vora-duration-med` | `240ms` | Card lift, dialog enter, panel slide |
| `--vora-duration-slow` | `560ms` | Hero crossfade, backdrop swap |
| `--vora-ease-out` | `cubic-bezier(0.16, 1, 0.3, 1)` | Default easing — the "soft landing" curve |

Guidelines:

- Hero artwork crossfades, never cuts.
- Cards lift on hover: `transform: scale(1.035) translateY(-2px)`, plus accent glow `box-shadow: 0 0 0 2px var(--vora-accent-soft)`.
- Parallax on detail-page backdrops: backdrop scrolls at 0.4× page scroll until it's masked off, then fades to canvas.
- Respect `prefers-reduced-motion`: disable parallax + scale, keep opacity crossfades.

## Cinematic backdrops

Two new responsibilities, both handled by a new `CinematicBackdrop` primitive:

1. **Page backdrop** — large image (1920×1080+ ideally) behind a page's header section. Top 70vh on `MediaDetailsPage` and `LibraryPage`, top 50vh on `Home` (when a feature is spotlighted). Always wrapped in a long gradient mask that fades from `var(--vora-bg-canvas)/0` at the top to `var(--vora-bg-canvas)/1` at the bottom over the lower 40% — this guarantees text legibility no matter the artwork.
2. **Template canvas image** — already supported in `ThemeManifest.backgrounds.canvas`. Renders behind the whole app at low opacity (0.06–0.12) with an optional tint. Holiday templates lean on this hard.

The `CinematicBackdrop` API:

```tsx
<CinematicBackdrop
    src={mediaItem.backdropUrl}
    intensity="hero"       // 'hero' | 'detail' | 'ambient'
    parallax              // optional
    transitionKey={mediaItem.id}  // forces crossfade when this changes
/>
```

## Primitives (`components/Client/Primitives/`)

New folder, mirrors `components/Admin/Primitives/`. Authored once, used everywhere on the client.

| Primitive | Purpose |
| --- | --- |
| `PageHeader` | Title + subtitle + action slot. Optional hero backdrop. Used by every client page. |
| `Hero` | Full-bleed featured area used on Home. Composes `CinematicBackdrop` + title + CTA. |
| `CinematicBackdrop` | See above. |
| `MediaPoster` | 2:3 portrait card. Title + meta below on hover/focus. Progress bar overlay. Replaces today's `MediaCard` for movies/TV/albums. |
| `MediaStill` | 16:9 landscape card. Used for episodes, recordings, Live TV programs, playlist covers. |
| `MediaRail` | Horizontal-scroll rail with snap, momentum, edge gradient fade. Replaces today's `MediaRow`. Includes a peek-right indicator and keyboard arrows. |
| `Chip` | Filter pill. Selected state uses `accent-soft`. |
| `Tabs` | Underlined tab bar. Replaces hand-rolled `border-b-2` patterns. |
| `EmptyState` | Centered glyph + title + body + optional CTA. |
| `Glass` | A frosted-surface wrapper (`backdrop-filter: blur(18px)` + `bg-glass`). Used by topbar, the player chrome panel, and popovers. |
| `QualityPanel` | Slide-in right panel for video track/quality selection. Replaces raw `<select>`s in player. |
| `NowPlayingBar` | The persistent bottom-of-screen music bar. Tappable to expand into full-screen Now Playing. |
| `LetterRail` | Vertical right-edge A–Z index for `LibraryPage` (refactored out of inline implementation). |

All composable with Tailwind utility classes; tokens consumed via `var(--vora-*)`.

## Iconography

Keep using stroke-style icons. We do not switch icon libraries — too much churn. Standard sizes: `18` inline, `20` rail nav, `24` action buttons, `32` empty-state glyphs. Always `stroke-width: 1.5`.

## Accessibility

- All interactive elements have visible focus rings: `box-shadow: 0 0 0 3px var(--vora-accent-soft)`.
- Color is never the only carrier of state — pair with icon/label.
- Hero backdrops always pair with a solid scrim under text. Contrast ratio target 4.5:1 minimum on body, 3:1 on display text per WCAG AA.
- `prefers-reduced-motion` disables parallax and card scale. Crossfades remain.
- Keyboard: every rail/grid supports arrow keys, `Home`/`End`. Player keyboard map documented in `page-redesigns.md`.

## What is removed

- Hand-rolled tabs (`border-b-2` pairs across `LibraryPage`, `AudioHubPage`, `CollectionsPage`, `PlaylistsPage`) — replaced by `Tabs`.
- Hand-rolled empty states — replaced by `EmptyState`.
- Raw `<select>` dropdowns in the player and Settings — replaced by `QualityPanel` and a new themed `Select` input variant.
- Hardcoded `bg-[#1e1f22]` and `border-gray-800` chains in `SettingsPage` — replaced by `var(--vora-bg-surface)` + `vora-card`.

## Scoping and shell

- `MainLayout` and `AuthLayout` set `data-vora-client=""` on their outer wrappers. This is what scopes the `[data-vora-client] .vora-button-*` / `.vora-input` / scrollbar / typography rules in `tokens.css`. Any new top-level page that lives outside those layouts (auth, profile picker, fullscreen player) MUST add `data-vora-client=""` itself.
- The admin shell uses `data-vora-admin` on its outer wrapper. Admin and client tokens are kept in separate selectors so they don't bleed across surfaces.
- The sidebar wordmark is the chevron-V SVG + lowercase "ora" lockup, with the chevron filled by a `--vora-accent-text → --vora-accent-500` gradient. The wordmark recolors per template.

## Player neighbors (prev / next episode)

The video player exposes prev/next-episode buttons when a TV episode is playing. The data comes from `mediaService.getUpNext(...)` which now returns both `nextItem` and `previousItem` on `UpNextResultVM`. Movies and standalone media return both as null and the buttons don't render. The same fields also drive prev/next inside a playlist context (`contextType=playlist`, `contextId=<playlistId>`). The `EpisodeNavButton` primitive in `Controls/PlayerButtons.tsx` is shared by both the fullscreen and minimized chrome.

## Modal stacking

- The MainLayout header is `z-[100]`. Sidebar dropdowns / chevron menu sit at `z-[105]–z-[110]`.
- The `Modal` primitive defaults to `z-[200]` and supports `z-[210]` for nested modals. Any handwritten overlay (`<div className="fixed inset-0 …">`) must also use `z-[200]+` or it will sit under the header.
- Dialogs (`useDialog`) use `z-[1000]`; full-screen players use `z-[99999]`. Don't sit anything else in that range.

## Brand assets

- `public/favicon.svg` — square Vora mark on a dark rounded background. Used as the browser/tab favicon.
- `public/vora-mark.svg` — bare chevron V with transparent background. Use anywhere on a non-white surface.
- `public/vora-logo.svg` — chevron V + "ora" lockup. Used by the sidebar header.
- The chevron gradient ties to `--vora-accent-*` so the brand picks up the active template's accent automatically.
