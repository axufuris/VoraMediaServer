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

`pages/Admin/LibraryUpdatesPage.tsx` at `/admin/library-updates` (and `/admin/server/:serverId/library-updates`), in the sidebar under Library. Radarr, Sonarr, Lidarr or any system that can send a webhook tells Vora when a file is imported, upgraded, renamed or deleted, so the library updates at once and a library's real-time watching can be turned off. Each link is a **webhook connection**: the admin adds as many as they like, each made from a **template** (Radarr, Sonarr, Lidarr or Custom). It works like Request Servers: a list, and an add/edit form. What the server does with a delivery is in `docs/media-webhooks.md`; the page only configures it.

The page is a short intro, the **connection tiles**, then **Recent deliveries**, all under `components/Admin/LibraryUpdates/`:

- **Tiles** (`ConnectionTiles.tsx`): a wrapping grid (`grid-cols-[repeat(auto-fill,minmax(14rem,1fr))]`) of compact button tiles, so Recent deliveries stays on screen with a few connections. A tile shows the connection's name, its template name, a `Ready` / `Off` badge (`connectionStatus`) and a last-delivery line (`lastDeliveryText`): `Last: 5 min ago, ok`, `Last: 12 min ago, failed` (the failure is in words, and bold in the danger colour) or `Nothing received yet`. The relative time takes `now` from the page, which keeps it in state and ticks it every minute (no `Date.now()` in render). The last tile is **Add connection**. A tile opens the edit modal.
- **Recent deliveries** (`RecentDeliveries.tsx`): time (`formatDateTime`), connection name (+ instance), event, path and outcome, newest first. The outcome carries an icon and the word OK or Failed, never colour alone. A **Filter by connection** dropdown (`All connections` by default) refetches with `connectionId`; **Refresh** reloads the list (with the same filter) and the tiles. Empty: `Nothing received yet. Press Test in the app, or send a sample.` The table scrolls sideways on phones (`max-md:min-w-[44rem]` inside `overflow-x-auto`). The page keeps the answer with the key (server + filter) it was fetched for and shows a skeleton until the current key has one. A note says the list clears on a server restart, since the server keeps it in memory.
- **The modal** (`ConnectionModal.tsx`, the admin `Modal` with `surface="light"`; one component for Add and Edit; it scrolls inside `max-md:max-h-[calc(100dvh-2rem)]`; Escape is ignored while one of its confirm dialogs is up). **Add** starts with a **Template** dropdown (names from the server, the chosen one's description under it; the name follows the template's `defaultConnectionName` until the admin types their own, the settings restart from the new template's defaults). When the template has a `requestServerProviderId` and the admin has Request Servers whose `webhookProviderId` is that template, **Fill in from a Request Server** (`RequestServerFill.tsx`) asks the server for that Radarr/Sonarr's root folders and fills the path mappings (and the name, only while it is still the template's default); it confirms through `useDialog().confirm` before replacing text already in the box, and shows `error` without filling anything. Saving creates the connection, then the same modal shows its **address and secret** (`created` state) with Copy buttons and the setup steps, instead of closing; **Done** closes it. **Edit** has: Name; the **On** and **Move deleted files to Media Trash** switches (`ConnectionSwitch.tsx`, one `role="switch"` button row like Edit Collection's, helper text that deletes the app reports are ignored when it is off); the read-only **address** (`window.location.origin` + `receivePath`, or the managed server's vault address on a multi-server page, `receiveUrl`); the **secret** (`SecretField` in `ConnectionAccess.tsx`: masked, Reveal / Hide, Copy, **Regenerate** after a confirm that the app's connection must be updated; read from `GET connections/{id}` when the modal opens); **Path mappings** (one `from => to` per line); the template's own settings (`TemplateSettings.tsx`); **Setup instructions** (`SetupInstructions.tsx`, the template's `setupInstructions` split on new lines into a numbered list, collapsible, open for a new connection and closed when editing); **Try a sample payload** (`SamplePayload.tsx`: paste a payload, **Run** sends the unsaved values to `/preview` and lists the event, instance and each change with its kind, reported and mapped path or note, library and whether it is on disk; nothing is queued); and **Delete connection** (confirm, edit only). **Save** is disabled while the name is empty or a shown required setting is. A 400 `{ error }` from the server is shown in the modal (`webhookErrorMessage`).
- **Template settings** come from the template's `settings` definitions (`connectionForm.ts`): `text`, `textarea`, `select` (each `options` entry is `value` or `value=Label`, split on the first `=`, the label shown and the value stored) and `library` (a dropdown of `libraryService.getLibraries`, stored as the library id, loaded only when a shown template has one). A required field has a `*`. `showWhen` (`key=value`) shows a field only while that setting has that value, and a hidden field is not required. A new connection starts from each `defaultValue`. All settings are sent on save and in a preview.
- **Focus is a fill**: every tile, button, switch and dropdown fills with `--vora-accent-500` on keyboard focus and drops the ring (`FOCUS_FILL`, `SELECT_FOCUS` for a dropdown, which also drops `.vora-input`'s focus shadow, in `libraryUpdates.ts`). Children with their own colour flip to `--vora-accent-contrast` with `group-focus-visible:` (`FOCUS_CONTRAST_TEXT`). The modal's close button takes the same through `ModalHeader`'s `closeClassName`. **Save** and **Done** are `vora-button-primary`, already accent-filled, so their focus fill is `--vora-accent-active` (`FOCUS_FILL_PRIMARY`) to stay visible; the other buttons are `vora-button-secondary`.
- Phones: the grid is one column when it cannot fit two, the address and secret rows wrap, the footer wraps, and the modal body drops to `max-md:p-4`; desktop is unchanged.

The calls are in `api/System/webhookAdminService.ts`, all admin only, under `/api/admin/webhooks` (each takes the optional `serverId`): `GET /providers` (`getProviders`: the templates), `GET /connections` and `GET /connections/{id}` (`getConnections`, `getConnection`: the detail adds the `secret`; the list never does), `POST /connections` (`createConnection`: 201 with the connection and its generated `secret`), `PUT /connections/{id}` (`updateConnection`), `POST /connections/{id}/secret` (`regenerateSecret`), `DELETE /connections/{id}` (`deleteConnection`), `GET /deliveries?limit=100&connectionId=` (`getDeliveries`), `POST /preview` (`preview`: a dry run on a pasted payload), `GET /request-servers` (`getRequestServers`: the Radarr/Sonarr Request Servers and the template that fits each) and `GET /request-servers/{id}/mappings` (`getRequestServerMappings`: that app's root folders, suggested mappings, the roots it could not match, or an `error`).

## Request Queue

`pages/Admin/RequestsPage.tsx` keeps **movie and show requests apart**: a Movies tab and a Shows tab (the shared `Tabs` strip), each counting what is pending, and under them the Pending / Processing / Available pills, counted for the type on show. A show is a request whose `type` is `TvShow`, in any case; everything else is a movie. It opens on Movies, or on Shows when only shows are waiting; after that, reloads (an approval, a delete) leave the tab alone.

**The sidebar's Request Queue entry carries a red count of the requests waiting for a decision**, both kinds together (`GET /api/requests/pending-count`, admin only, `RequestStatus.Pending` only), capped at `99+` and not drawn at zero. `usePendingRequestCount` reads it on mount and again on `RequestQueueChanged`, which `RequestManager` sends to the admins group (`docs/realtime.md`) when a request is created, approved or deleted, so a new request shows up without a refresh. `SidebarV2` gives `NavItem` the count for `/admin/requests` only.

**Approve & Send says it worked.** `useActionToast` (`components/Admin/Primitives/ActionToast.tsx`) returns a `toast` element to render at the end of the page and a `showToast(message)`; it is a polite live region, bottom right, gone after a few seconds. Approving shows `Approved "Title" and sent to Radarr.` (the default server for the request's type, or just `and sent.` when none is named). A failure keeps its alert and shows no toast.
