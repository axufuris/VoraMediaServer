# Admin UI (`Vora.Web`)

The admin side of the frontend: the admin shell and its design tokens, the admin theme system, and the admin nav data. The folder layout, dialogs, and the rules every frontend change follows are in [`docs/frontend-conventions.md`](frontend-conventions.md).

## Admin shell + design tokens

The admin section (`/admin` and `/admin/server/:serverId`) is rendered inside `components/Admin/Shell/AdminShell.tsx`, which composes the top app bar, the sidebar (`SidebarV2`), and the routed page outlet. The old `layouts/AdminLayout.tsx` is a deprecated stub (look for `// DEPRECATED` or `Safe to git rm`) and is no longer referenced.

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

**Folder fields** use `FolderBrowser/FolderPathInput`: a path box plus **Browse**, which opens `FolderBrowserModal` on the server's file system. For a single-path setting the picked folder fills the box (`onChange`). A form that collects a list of folders passes `onBrowseSelect`, so a picked folder goes straight into the list and the box stays for typed or pasted paths: the library folders on Create Library and Manage Library do, through the same duplicate check as **Add path**.

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

`components/Admin/Shell/adminNavData.tsx` exports `ADMIN_NAV` (the canonical list of admin pages with `label`, `pathTemplate`, `icon`, `section`, optional `keywords` for the palette, optional `requires: 'ai'` runtime gate). Both `SidebarV2` and `SearchPalette` consume this list. **Add a new admin page in exactly one place** — it will appear in the sidebar with the right icon/section and in the Cmd-K palette with the right keywords automatically. Same file exports `Icons` (the SVG-path map) and `resolveAdminPath(template, serverId)` (the `/admin/...` → `/admin/server/<id>/...` rewriter).

The sidebar's **Sign-in Providers** entry (`/admin/sign-in-providers`, `key` icon) is one such line in `ADMIN_NAV`; the page is described in `docs/sso.md` (The web app).

## Library Updates

`pages/Admin/LibraryUpdatesPage.tsx` at `/admin/library-updates` (and `/admin/server/:serverId/library-updates`), in the sidebar under Library. Radarr, Sonarr and Lidarr tell Vora through a Webhook connection when a file is imported, upgraded, renamed or deleted, so the library updates at once and a library's real-time watching can be turned off. The `MediaWebhook` plugins (`radarr_webhook`, `sonarr_webhook`, `lidarr_webhook`) hold the `secret` and `path_mappings` settings; the page is where they are set up.

- **One card per provider** (`components/Admin/LibraryUpdates/WebhookProviderCard.tsx`), titled with the provider's name minus "Webhook". A status badge says `Off` (the plugin is off, and the card points to Plugins), `Needs a secret`, or `Ready`. The address to paste into the app is `window.location.origin` + the provider's `receivePath`, read-only with a Copy button and a note that the app must be able to reach it (inside Docker that is usually the container name and port). Under it: the numbered setup steps, **Generate secret**, and the generic `PluginSettingsForm` for the plugin (secret + path mappings; the path-mapping help text is the setting's own description).
- **Generate secret** asks first, through `useDialog().confirm`, when a secret already exists (the app's connection has to be updated), then calls the server, shows the new secret once in a highlighted box with a Copy button, and remounts the settings form (a React `key`) so it reloads the stored secret. The provider list is read again so the badge follows.
- **Recent deliveries** (`RecentDeliveries.tsx`): time (`formatDateTime`), source (provider name + instance), event, path and outcome, newest first, with a Refresh button and an empty state. The outcome carries an icon and the word OK or Failed, never colour alone. The table scrolls sideways on phones (`max-md:min-w-[44rem]` inside `overflow-x-auto`). The server keeps the list in memory, so it clears on a restart.
- Every button on the page fills with `--vora-accent-500` on keyboard focus and drops the ring (`FOCUS_FILL` in `libraryUpdates.ts`). The buttons are `vora-button-secondary`: the primary one is already accent-filled, so a focus fill would not show.

The calls are in `api/System/webhookAdminService.ts`, all admin only: `GET /api/admin/webhooks` (`getProviders`: id, name, description, `libraryKind`, `receivePath`, `isConfigured`, `isEnabled`, never a secret), `GET /api/admin/webhooks/deliveries?limit=100` (`getDeliveries`) and `POST /api/admin/webhooks/{providerId}/secret` (`generateSecret`: stores a new random secret and returns it once).

## Request Queue

`pages/Admin/RequestsPage.tsx` keeps **movie and show requests apart**: a Movies tab and a Shows tab (the shared `Tabs` strip), each counting what is pending, and under them the Pending / Processing / Available pills, counted for the type on show. A show is a request whose `type` is `TvShow`, in any case; everything else is a movie. It opens on Movies, or on Shows when only shows are waiting; after that, reloads (an approval, a delete) leave the tab alone.

**The sidebar's Request Queue entry carries a red count of the requests waiting for a decision**, both kinds together (`GET /api/requests/pending-count`, admin only, `RequestStatus.Pending` only), capped at `99+` and not drawn at zero. `usePendingRequestCount` reads it on mount and again on `RequestQueueChanged`, which `RequestManager` sends to the admins group (`docs/realtime.md`) when a request is created, approved or deleted, so a new request shows up without a refresh. `SidebarV2` gives `NavItem` the count for `/admin/requests` only.

**Approve & Send says it worked.** `useActionToast` (`components/Admin/Primitives/ActionToast.tsx`) returns a `toast` element to render at the end of the page and a `showToast(message)`; it is a polite live region, bottom right, gone after a few seconds. Approving shows `Approved "Title" and sent to Radarr.` (the default server for the request's type, or just `and sent.` when none is named). A failure keeps its alert and shows no toast.
