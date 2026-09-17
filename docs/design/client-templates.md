# Client templates

A parallel system to the admin theme system already documented in `docs/admin-theme-bundles.md`. Same manifest schema (`ThemeManifest`). Same bundle layout, swapped folder name (`Templates/<id>/`). Separate registry, endpoint group, persistence, and SignalR event.

## Why parallel rather than reused

Sharing one registry between admin and client surfaces seems tempting but produces poor UX: a manifest tuned for admin readability (high-contrast neutrals) usually looks wrong against cinematic artwork, and vice versa. Plugin authors should be able to ship a "Christmas" template that targets the client without polluting admin pickers.

Plugins can ship both — a single plugin can drop manifests into `Themes/<id>/` (admin) and `Templates/<id>/` (client). The bundle loader scans both folders.

## Manifest

Reuse `Vora.Web/src/theme/types.ts`'s `ThemeManifest` unchanged. Two additions to `ThemeBackgrounds` so templates can paint surfaces that admin themes don't need:

```ts
export interface ThemeBackgrounds {
    canvas?: BackgroundSlot | null;
    pageHeader?: BackgroundSlot | null;
    playerScrim?: BackgroundSlot | null;   // new: behind the player chrome
    loginCanvas?: BackgroundSlot | null;   // new: behind /login, /register, /profiles
}
```

Backwards-compatible — both new fields are optional, existing admin manifests are unaffected. `applyTheme.ts` gains two CSS-var writes:

- `--vora-bg-player-scrim-image`
- `--vora-bg-login-canvas-image`

Both consumed by component-local CSS in the player chrome and the auth pages.

## Backend

### Entities

Add to `Vora.Domain/Entities/Settings/ServerSetting.cs`:

```csharp
public string DefaultClientTemplateId { get; set; } = "vora-cinema";
```

Add to `Vora.Domain/Entities/Users/UserProfile.cs` (the profile entity — note: named `UserProfile`, not `Profile`):

```csharp
public string? ClientTemplateId { get; set; }                   // user's long-term default
public string? ScheduleOverrideTemplateId { get; set; }         // override for the schedule below
public Guid? ScheduleOverrideScheduleId { get; set; }           // which schedule this override belongs to
```

The override fields are nullable; null means "use whatever the schedule says". When a profile picks a template while a schedule is active, both override fields are written together. When no schedule is active, the user's pick writes only `ClientTemplateId`.

### Endpoints — `Vora.Api/Endpoints/TemplateEndpoints.cs`

| Method | Path | Auth | Returns |
| --- | --- | --- | --- |
| `GET` | `/api/templates/active` | profile | `{ templateId, source: 'schedule' \| 'override' \| 'profile' \| 'default', schedule?: ActiveScheduleVM }` |
| `GET` | `/api/templates` | profile | `TemplateMetaVM[]` |
| `PUT` | `/api/templates/active` | profile | `{ templateId }` — server figures out whether to store as `ScheduleOverride*` or `ClientTemplateId` |
| `DELETE` | `/api/templates/active` | profile | resets to `null` (falls back to default) |
| `GET` | `/api/templates/{templateId}/manifest` | profile | full `ThemeManifest` JSON (built-ins respond 404 — the frontend has them bundled) |
| `GET` | `/api/templates/{templateId}/assets/{*assetPath}` | **anonymous** | static asset stream — same pattern as theme assets, so CSS `background-image` URLs work without auth |
| `GET` | `/api/admin/templates/schedules` | admin | `TemplateScheduleVM[]` |
| `POST` | `/api/admin/templates/schedules` | admin | create — see `template-scheduling.md` |
| `PUT` | `/api/admin/templates/schedules/{id}` | admin | update |
| `DELETE` | `/api/admin/templates/schedules/{id}` | admin | delete |
| `PUT` | `/api/admin/templates/default` | admin | sets `ServerSetting.DefaultClientTemplateId` |
| `POST` | `/api/admin/templates/rescan` | admin | rescan plugin templates |

Endpoints stay thin per `docs/backend-conventions.md`. All logic lives in `Vora.Application/Templates/`:

- `IClientTemplateRegistry` — aggregates built-in + plugin templates.
- `IClientTemplateBundleLoader` — same `manifest.json` validation as `ThemeBundleLoader`.
- `IClientTemplateManager` — `GetActiveAsync(profileId)`, `SetActiveAsync(profileId, templateId)`, `ClearActiveAsync(profileId)`.
- `IClientTemplateScheduleManager` — schedule CRUD + `GetActiveScheduleAsync(now)`.

### Resolution algorithm

Single function: `IClientTemplateManager.GetActiveAsync(profileId)`.

```
now = UtcNow
schedule = scheduleManager.GetActiveScheduleAsync(now)

if schedule != null:
    if userProfile.ScheduleOverrideScheduleId == schedule.Id and userProfile.ScheduleOverrideTemplateId != null:
        return (templateId: userProfile.ScheduleOverrideTemplateId, source: 'override', schedule)
    return (templateId: schedule.TemplateId, source: 'schedule', schedule)

if userProfile.ClientTemplateId != null:
    return (templateId: userProfile.ClientTemplateId, source: 'profile')

return (templateId: serverSettings.DefaultClientTemplateId, source: 'default')
```

Notes:

- The override is keyed to the **specific schedule** by id. When the schedule ends or a different schedule activates, the override naturally goes stale (the `if` branch fails) and resolution falls through to the profile default. This is the exact behavior the user asked for.
- We do **not** mutate the profile when a schedule ends — the stale override row just stops being used. A nightly cleanup worker (`ClientTemplateOverrideCleanupWorker` in `Vora.Infrastructure/Workers/`) zeroes out override fields whose `ScheduleOverrideScheduleId` no longer matches an active or future schedule. This keeps the row tidy but is **not** required for correctness.

### SetActiveAsync logic

When a profile chooses a template via `PUT /api/templates/active`:

```
schedule = scheduleManager.GetActiveScheduleAsync(now)

if schedule != null and chosenTemplateId != schedule.TemplateId:
    // user is overriding the scheduled template
    userProfile.ScheduleOverrideTemplateId = chosenTemplateId
    userProfile.ScheduleOverrideScheduleId = schedule.Id
else if schedule != null and chosenTemplateId == schedule.TemplateId:
    // user explicitly chose to "match the schedule" — clear any prior override
    userProfile.ScheduleOverrideTemplateId = null
    userProfile.ScheduleOverrideScheduleId = null
else:
    // no active schedule — this becomes the profile's long-term default
    userProfile.ClientTemplateId = chosenTemplateId
```

If the user picks the *same* template that's already scheduled, we clear the override fields — there's nothing to override.

### SignalR

New event `ClientTemplateChanged(profileId, templateId)` on `IClientNotifier`, scoped per-profile. The frontend's `ClientTemplateProvider` subscribes via `useSignalREvent`. Schedule activation/deactivation broadcasts to **all profiles** on the server.

## Frontend

### `theme/ClientTemplateProvider.tsx`

Modeled on `ThemeProvider.tsx`. Hangs inside `BrowserRouter`, mounted between `ThemeProvider` and the route tree so the admin theme provider keeps owning the `/admin` surface and the client provider owns everything else.

```
DialogProvider → PlayerProvider → BrowserRouter → ThemeProvider → ClientTemplateProvider → Routes
```

Lifecycle:

1. First-paint: read `vora_client_template_id` from localStorage. Apply if a built-in manifest matches. Otherwise apply the cinema default.
2. After mount: `GET /api/templates/active`. If different from first-paint, fetch the manifest (built-ins inline, plugins via `/api/templates/{id}/manifest`), apply, write localStorage.
3. Render an active-schedule banner ribbon at the top of every client page when `source === 'schedule'` (component: `Client/Primitives/ScheduledTemplateBanner`).
4. `setActive(id)` is async, optimistic, with revert on backend failure — exactly the admin pattern.
5. SignalR: subscribe to `ClientTemplateChanged`. If a schedule activates while the user is browsing, the banner appears and the template applies live.

### `api/System/clientTemplateService.ts`

Mirrors `themeService.ts` 1:1 in shape: `getActive`, `getAll`, `setActive`, `clear`, `getManifest`, plus admin-only `getSchedules`, `createSchedule`, etc.

### Built-in client templates (ships in repo)

Four built-ins, all in `Vora.Web/src/theme/clientTemplates/`:

| id | feel |
| --- | --- |
| `vora-cinema` | The default. Deep canvas, amber accent, subtle vignette. |
| `vora-noir` | Pure black canvas, cool steel accent, high contrast. |
| `vora-velvet` | Burgundy canvas, gold accent, warm sepia-tinted artwork tints. |
| `vora-aurora` | Deep navy with teal accent and an aurora-gradient canvas background image. |

Manifest registration mirrors what admin does: list in `BUILT_IN_CLIENT_TEMPLATES`, add `ThemeMetaVM` entries to `IClientTemplateRegistry`, add swatches to the settings page swatch map.

### Settings → Templates tab UI

Card grid (`grid-cols-2 lg:grid-cols-3`), each card is a `ThemeCard` analog with:

- Background swatch strip (or `preview` image if the manifest defines one).
- Name, description, version, "Built-in" / "Plugin" badge.
- "Active" badge on the resolved template.
- Action button: "Set as default" (no active schedule) or "Use during {{scheduleName}}" / "Revert to schedule" (schedule active).

A `ScheduledTemplateBanner` sits at the top of the tab when a schedule is active, showing schedule name, end time, and a "Revert to my default" action that clears any override.

## Migrations

One PMC migration: `AddClientTemplates`.

- `ServerSetting.DefaultClientTemplateId` (default `"vora-cinema"`).
- `UserProfile.ClientTemplateId` (nullable).
- `UserProfile.ScheduleOverrideTemplateId` (nullable).
- `UserProfile.ScheduleOverrideScheduleId` (nullable, FK to `ClientTemplateSchedule.Id` with cascade-null on delete).
- New table `ClientTemplateSchedules` — see `template-scheduling.md`.

**Do not** hand-write this migration. Run `add-migration AddClientTemplates` in the VS Package Manager Console after the entity changes are merged.
