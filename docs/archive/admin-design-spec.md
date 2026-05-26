# Vora Admin Design Spec

The reference document for the Vora admin visual language. Phase 1 hardcodes the values here; Phase 2 lifts them into a token system.

## Design principles

1. **Editorial, not dashboard-y.** Most admin tools look like ops dashboards. Vora is a media product. The admin should feel curated — generous white space, real typography, considered hierarchy — not a sea of charts.
2. **Quiet by default, loud when something matters.** Color is reserved for action and state. Surfaces, borders, and most text live in a tight neutral palette. The accent earns its keep by being rare.
3. **Glanceable.** A returning admin should see "is anything broken?" in under a second from anywhere in the shell.
4. **Dense without being cramped.** Modeled on Linear and Stripe — high information density per square inch, but every element has air around it.
5. **Templatable from day one.** Every color, radius, and shadow goes through a semantic token name (even when hardcoded in Phase 1) so Phase 2 is a lift, not a rewrite.

## Palette — Vora Default (light)

Built on Tailwind's **stone** family (warm neutrals) with **amber** as the accent. Stone reads editorial; amber preserves brand orange DNA while feeling more refined than raw orange-500.

### Surfaces

| Token | Hex | Use |
|---|---|---|
| `bg.canvas` | `#fafaf9` (stone-50) | App background |
| `bg.surface` | `#ffffff` | Cards, sidebar, top bar |
| `bg.raised` | `#ffffff` + shadow-sm | Floating cards, modals |
| `bg.sunken` | `#f5f5f4` (stone-100) | Recessed surfaces, table headers, code blocks |
| `bg.overlay` | `rgba(28, 25, 23, 0.6)` | Modal scrim |

### Borders

| Token | Hex | Use |
|---|---|---|
| `border.subtle` | `#e7e5e4` (stone-200) | Default card and section borders |
| `border.strong` | `#d6d3d1` (stone-300) | Inputs, dividers that need to feel present |
| `border.focus` | `var(--accent.500)` | Focus rings |

### Text

| Token | Hex | Use |
|---|---|---|
| `text.primary` | `#1c1917` (stone-900) | Headings and primary body |
| `text.secondary` | `#44403c` (stone-700) | Sub-headings, secondary body |
| `text.muted` | `#78716c` (stone-500) | Helper text, captions, metadata |
| `text.disabled` | `#a8a29e` (stone-400) | Disabled labels |
| `text.inverse` | `#fafaf9` | Text on accent / dark backgrounds |

### Accent (Amber)

| Token | Hex | Use |
|---|---|---|
| `accent.500` | `#d97706` (amber-600) | Primary action surfaces |
| `accent.hover` | `#b45309` (amber-700) | Hover on primary actions |
| `accent.soft` | `#fef3c7` (amber-100) | Active nav background, soft pills |
| `accent.softHover` | `#fde68a` (amber-200) | Hover on soft pills |
| `accent.text` | `#92400e` (amber-800) | Accent text on light surfaces |
| `accent.contrast` | `#ffffff` | Text on `accent.500` |

### Semantic

| Token | Hex |
|---|---|
| `success.500` | `#16a34a` (green-600) |
| `success.soft` | `#dcfce7` (green-100) |
| `warning.500` | `#eab308` (yellow-500) |
| `warning.soft` | `#fef9c3` (yellow-100) |
| `danger.500` | `#dc2626` (red-600) |
| `danger.soft` | `#fee2e2` (red-100) |
| `info.500` | `#0284c7` (sky-600) |
| `info.soft` | `#e0f2fe` (sky-100) |

## Typography

- **Sans:** `Inter, ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`
- **Mono:** `"JetBrains Mono", ui-monospace, "SF Mono", Menlo, monospace`
- **Base size:** 14px (admin) / 16px (client)
- **Line height:** 1.5 body, 1.2 headings
- **Scale:** `xs 11 / sm 12 / base 14 / md 15 / lg 17 / xl 20 / 2xl 24 / 3xl 30 / display 36`
- Headings use `font-weight: 600` (semibold), not `700` (bold). Bold is reserved for emphasis.

## Spacing, radii, shadows

- **Radii:** `sm 4px`, `md 6px`, `lg 10px`, `xl 14px`, `pill 9999px`. Cards default to `lg`. Pills/badges use `pill`. Inputs use `md`.
- **Shadows:**
  - `shadow.sm` — `0 1px 2px rgba(28,25,23,0.04), 0 1px 2px rgba(28,25,23,0.06)` (cards)
  - `shadow.md` — `0 4px 8px rgba(28,25,23,0.05), 0 2px 4px rgba(28,25,23,0.06)` (raised cards on hover)
  - `shadow.lg` — `0 12px 24px rgba(28,25,23,0.08), 0 4px 8px rgba(28,25,23,0.06)` (popovers, dropdowns)
  - `shadow.overlay` — `0 24px 48px rgba(28,25,23,0.18)` (modals)
- **Spacing scale:** Tailwind's default (4px base). Default page padding: `p-8`. Section gap: `space-y-8`. Card padding: `p-5` to `p-6`.

## Layout

### App shell

```
+--------------------------------------------------------+
| Top app bar (h=56)                                     |
+-----+--------------------------------------------------+
| Sb  | Page content (scrolls)                           |
|     |                                                  |
| 240 |   +-- PageHeader (sticky, h~80) ------------+    |
|     |   |                                         |    |
|     |   +-- PageTabs (optional) -----------------+     |
|     |   |                                         |    |
|     |   +-- Page body (cards, sections, etc.) ---+    |
+-----+--------------------------------------------------+
```

- **Top app bar:** 56px tall, `bg.surface` with `border.subtle` bottom. Holds (left→right): server switcher, vertical divider, breadcrumb, spacer, global search trigger, activity pill, notification bell, account menu.
- **Sidebar:** 240px wide expanded / 64px rail. `bg.surface` with `border.subtle` right. Section header chips (small uppercase pills). Active item: 2px accent left-bar + `accent.soft` background + `accent.text` foreground.
- **PageHeader:** sticky beneath top bar. Holds title, optional breadcrumb (mobile only — on desktop the top-bar breadcrumb is canonical), description slot, primary-action slot. Border-bottom hairline.
- **PageTabs:** below PageHeader when present. Matches `FeatureTabs` API but renamed.

### Responsive

- ≥1280px: full sidebar + content max-w-7xl
- 768–1280px: full sidebar + content max-w-6xl
- <768px: sidebar collapses to icon rail, content padding `p-4`

## Component vocabulary (Phase 1)

| Component | Purpose |
|---|---|
| `AdminShell` | Top bar + sidebar + outlet wrapper. Replaces `AdminLayout`. |
| `TopAppBar` | Top app bar including all elements above. |
| `ServerSwitcher` | Top-bar pill showing active server name + dropdown of all servers. |
| `Breadcrumb` | Top-bar element rendered from route or override. |
| `GlobalSearchTrigger` | Top-bar Cmd/Ctrl-K button (palette UI itself stubbed in wave 1). |
| `ActivityPill` | Live count of active streams + transcodes; clicks to dashboard. |
| `AccountMenu` | Top-bar avatar + dropdown (profile, sign out). |
| `SidebarV2` | Sidebar with section chips, status dots, accent bar. |
| `PageHeader` | Sticky header strip; props `title`, `description`, `breadcrumb`, `actions`. |
| `PageTabs` | Renamed `FeatureTabs`. Same API. |
| `Section` | `<section>` with consistent spacing + optional heading row. |
| `StatCard` | Compact metric tile — label, value, unit, optional trend, optional accent. |
| `EntityCard` | Card representing a domain object (library, plugin, user). |
| `ListCard` | Card containing a scrollable list of items (recent activity, etc.). |
| `HealthBadge` | Pill badge with status color (`ok`/`warn`/`error`/`info`). |
| `StatusDot` | 8px dot in semantic color. Used in sidebar and headers. |
| `EmptyState` | Centered illustration-less empty placeholder with optional CTA. |
| `DataTable` | Light table primitive: sortable headers, sticky top, zebra-free. |

## Interaction details

- **Hover lift on cards:** subtle `translate-y-[-1px]` + shadow upgrade on `EntityCard`. Not on `StatCard` (which is non-interactive).
- **Focus rings:** 2px `accent.500` outline with 2px offset on all interactive elements. Honor `:focus-visible`.
- **Motion:** 150ms ease-out for hover transitions; 200ms cubic-bezier(0.2,0.0,0.0,1) for tab/route changes. No bouncing.
- **Skeletons:** card-shaped placeholders in `bg.sunken` with a 1.5s shimmer. Used during initial load only — re-fetches preserve previous values.

## Dashboard composition (the proof point)

```
PageHeader: "Dashboard"  (no actions)
+-- Hero stat row (4 cols on lg, 2 cols on md, 1 col on sm) -----+
|   StatCard: Now Streaming                                       |
|   StatCard: CPU                                                 |
|   StatCard: Memory                                              |
|   StatCard: Total Bandwidth                                     |
+-- Two-column row (lg) ------------------------------------------+
|   ListCard: Now Playing (sessions, real-time)                  |
|   ListCard: Recent Activity (notifications + imports feed)     |
+-- Libraries strip ----------------------------------------------+
|   EntityCard per library, horizontal scroll on overflow        |
+-- System health card ------------------------------------------+
|   Per-subsystem status: storage, transcode queue, plugin health|
+-----------------------------------------------------------------+
```

Real data sources to wire in wave 1:
- Now Playing + CPU + RAM: `streamingAdminService` (already exists)
- Libraries strip: `libraryService.getLibraries` (already exists)
- Notification feed: `adminNotificationService.getRecent` (already exists)
- Plugin health: `pluginAdminService.getPlugins` filtered to disabled/error states

Stubbed for wave 1 (real wiring later):
- Storage % full (needs a storage endpoint)
- Transcode queue depth (needs a streaming endpoint)
- "Recent Imports" feed (needs an imports/scan log)

## Out of scope for wave 1

Light-mode dark-mode toggle (it's the user's job in Phase 3's theme picker), Cmd-K palette implementation (just the trigger), keyboard navigation rework, mobile-first overhaul.
