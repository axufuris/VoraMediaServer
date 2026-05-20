# Scheduled templates

Admin schedules a template to apply across all profiles during a window. Profiles can override during the window; their override expires when the window ends and falls back to their personal default.

## Entity

New table `ClientTemplateSchedules`:

```csharp
public class ClientTemplateSchedule
{
    public Guid Id { get; set; }
    public string TemplateId { get; set; } = null!;
    public string Name { get; set; } = null!;            // "Thanksgiving 2026"
    public DateTime StartsAt { get; set; }               // UTC
    public DateTime EndsAt { get; set; }                 // UTC
    public int Priority { get; set; }                    // higher wins when ranges overlap
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

Indexes: `(Enabled, StartsAt, EndsAt)` for the active-schedule lookup; `TemplateId` for cascade behavior if a template plugin is removed (we leave the schedule but disable it on resolution — see below).

EF configuration goes in a new `ConfigureClientTemplates(ModelBuilder)` helper called from `VoraDbContext`'s `OnModelCreating`. Mirror the pattern of existing `Configure*` partials.

## Active-schedule lookup

```sql
SELECT TOP 1 *
FROM ClientTemplateSchedules
WHERE Enabled = 1
  AND StartsAt <= @now
  AND EndsAt > @now
ORDER BY Priority DESC, StartsAt DESC;
```

EF Core equivalent in `ClientTemplateScheduleManager.GetActiveScheduleAsync(now)`. Returns `null` if none match. Result is cached for 30 seconds (in-memory) to avoid hitting the DB on every page load — that interval is short enough that a schedule activating "now" appears within half a minute even without SignalR.

When a schedule's `TemplateId` no longer resolves to a registered template (plugin uninstalled), `GetActiveScheduleAsync` filters it out and continues to the next candidate. The schedule row stays in place — the admin can fix the template id later.

## Admin scheduling UI

Lives at `pages/Admin/Templates/SchedulesPage.tsx` (new admin page; add one entry to `adminNavData.tsx` so it appears in the admin sidebar + Cmd-K palette).

Layout:

1. `PageHeader` with title "Template schedules" and action button "New schedule".
2. List of schedules grouped into **Active**, **Upcoming**, **Past**, **Disabled** with `EntityCard`s. Each card shows: name, template (with swatch), start/end (local time), priority, enable toggle, edit & delete actions.
3. `New schedule` opens a `Modal` (`Common/Modal`, `surface="light"` to pick up admin theme tokens). Fields:
   - Name (required, short text)
   - Template (picker — uses same `ThemeMetaVM[]` source as the client Templates tab)
   - Start date/time (local) — converted to UTC on submit
   - End date/time (local)
   - Priority (number, default 0; admin tooltip explains the overlap rule)
   - Enabled (toggle, default true)
4. Validation: end must be after start; warn if the range overlaps an existing higher-priority schedule.

## Live propagation

When a schedule is created, updated, or deleted, the manager broadcasts `ClientTemplateSchedulesChanged` via `IClientNotifier`. Connected clients' `ClientTemplateProvider`:

1. Receives the event.
2. Re-runs `GET /api/templates/active`.
3. If the resolved template id changed, fetches the new manifest and applies it.

At schedule **boundaries** (start, end), the server doesn't fire a SignalR push by default — there's no background trigger. Two options for that, both optional:

1. **Polling fallback** — `ClientTemplateProvider` re-checks `GET /api/templates/active` whenever the tab regains focus and on a slow 5-minute timer when the tab has focus. Cheap; covers the common case.
2. **Scheduled push (deferred)** — A `ClientTemplateBoundaryWorker` schedules `Timer`s for the next 24h of boundaries and fires `ClientTemplateChanged` at each. More precise but extra moving parts. Adopt only if the polling fallback feels laggy in real use.

Phase 4 ships option (1). Option (2) is a follow-up if needed.

## Edge cases

- **Two schedules with the same priority overlapping.** The newer (higher `StartsAt`) wins — `ORDER BY Priority DESC, StartsAt DESC`. Admin UI warns when configuring overlap.
- **Schedule references a template that was uninstalled.** Schedule is skipped during resolution; admin sees a yellow "Template not found" badge on the card.
- **A profile picks a different template right before a schedule starts.** Profile's pick lands in `ClientTemplateId` (no active schedule yet). When the schedule starts, resolution falls through `if schedule != null` and ignores `ClientTemplateId`. The user's pick is preserved and reappears after the schedule ends. ✓ matches user's spec.
- **A profile picks during a schedule and the schedule ends.** `ScheduleOverrideScheduleId` no longer matches an active schedule. Resolution falls through to `userProfile.ClientTemplateId` (their long-term default). ✓ matches user's spec.
- **A profile picks during a schedule, then another schedule activates back-to-back.** The override is keyed to the old schedule id; the new schedule doesn't match, so resolution returns the new schedule's template. The user can then choose to override the new schedule. ✓ keeps overrides scoped per-event.
- **Daylight saving boundary.** All schedule times stored UTC; the admin UI converts to/from local time. No special handling — UTC math is monotonic.

## Tests to write (phase 4)

- `ClientTemplateScheduleManagerTests` — overlapping priority resolution, plugin-missing skip.
- `ClientTemplateManagerResolutionTests` — table-driven over the spec rows: no schedule + no profile pick → default; schedule active + no override → schedule; schedule active + override → override; etc.
- `ClientTemplateManagerSetActiveTests` — schedule active + same template → clears override; schedule active + different template → stores override; no schedule + pick → stores long-term default.

## What this is **not**

- **Not per-profile schedules.** A schedule applies server-wide. A profile can opt out of a given schedule by setting an override, but admins can't say "this schedule applies only to user X."
- **Not template inheritance.** Schedules pick one template — they don't blend. The cinematic design language stays consistent because all templates share the same `ThemeManifest` shape.
- **Not a UI-builder.** Admins schedule existing templates. Authoring new templates is the plugin path (`Templates/<id>/manifest.json` + assets) — same workflow as admin theme bundles documented in `docs/admin-theme-bundles.md`.

## Feasibility recap (the user's question)

About one developer-day of backend work plus the admin scheduling page on the frontend. Notes:

- The trickiest piece — "user override sticks until the schedule ends, then reverts to their default" — falls out of keying the override to the schedule id. No special cleanup is required for correctness; the cleanup worker is purely cosmetic.
- No external scheduler required (no Quartz, no Hangfire). The DB-keyed comparison runs at request time and is cached for 30 seconds.
- The risk to flag: if the admin creates a schedule with a very narrow window (under a minute), the 30-second cache plus the 5-minute focus refresh could make the schedule "feel late" by up to 30s. That's acceptable for holiday-template use cases (Thanksgiving = a week long). If sub-minute precision ever matters, adopt option (2) above.
