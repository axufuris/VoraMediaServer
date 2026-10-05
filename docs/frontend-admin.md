# Admin UI (`Vora.Web`)

The admin side of the frontend: the admin shell and its design tokens, the admin theme system, and the admin nav data. The folder layout, dialogs, and the rules every frontend change follows are in [`docs/frontend-conventions.md`](frontend-conventions.md).

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
