# Admin reorganization + feature toggles plan

Three changes, designed together:

1. **Feature toggles** — a global on/off switch per server for seven features. When off the feature is hidden from client navigation (including Edit Navigation) and the corresponding backend endpoints refuse to serve.
2. **Admin sidebar reorganization** — group the flat list into clear sections so admins find related things together.
3. **Feature pages** — each toggleable feature gets a single admin page that combines its toggle, its settings, and its plugins. Plugins move out of the System Settings catch-all and into the feature page they belong to.

## Part 1 — Feature toggles

### What's toggleable

Seven features, each backed by a single `bool` on `ServerSetting`:

| Feature | Flag | What turning it off does |
| --- | --- | --- |
| Discover | `EnableDiscover` | Hides Discover nav. `GET /api/discovery/*` returns 403. Smart Lists that reference Discover rows fall through cleanly. |
| For You | `EnableForYou` | Hides For You nav. `GET /api/recommendations/*` returns 403. |
| Release Calendar | `EnableReleaseCalendar` | Hides Release Calendar nav. `GET /api/calendar/*` returns 403. |
| Live TV | `EnableLiveTv` | Hides Live TV nav + Audio hub Radio tab also hides IPTV-Radio sources. `GET /api/iptv/*` returns 403 for non-admin. Channels disappear from search. DVR auto-disables. |
| DVR | `EnableDvr` | Hides DVR nav + the Record button on the Live TV guide. Existing recordings stay accessible only via admin. Auto-disabled when Live TV is off. |
| Internet Radio | `EnableInternetRadio` | Hides the Audio hub's Radio tab. `GET /api/iptv/client/playlists` filters out `Radio`-kind playlists. |
| Podcasts | `EnablePodcasts` | Hides the Audio hub's Podcasts tab. `GET /api/podcasts/*` returns 403. |

All flags default to `true` so existing installs see no change.

### Backend shape

- Add the seven `bool` columns to `ServerSetting` (default `true`). One PMC migration.
- A new `FeatureFlagsVM` view model returns just the seven flags for client consumption: `GET /api/server/features`. Cached briefly client-side.
- Admin-only `PUT /api/server/features` to update them. Goes through `SystemSettingsAdminService`.
- Each toggleable endpoint group gets a `[RequireFeature("Discover")]`-style guard (implemented as a small filter or an extension method on `RouteGroupBuilder`). Returns 403 when the corresponding flag is `false`.

### Frontend nav gating

`MainLayout.tsx` already builds `baseItems` from a static list. Switch it to filter by feature flag, tied to the **active server**. If the active server has Discover off, the Discover nav entry doesn't render at all — not in the main sidebar, not in Edit Navigation. When the user switches their active server, the nav refreshes against the new server's flags.

(Note: this means a multi-server user whose active server is Server A with Discover off won't see Discover even if Server B has it on. To reach Server B's Discover they'd switch active server first. Tell me if you want any-server-on instead.)

Feature-disabled items are completely absent from the Manage Sidebar list — they can't be pinned, unpinned, or reordered.

### What happens to in-flight state

- Live TV off mid-stream: the player keeps playing (don't kick the user). Future channel switches fail gracefully.
- DVR off: scheduled recordings stop firing on the next sync; in-progress recordings finish.
- Podcasts off: episodes mid-playback finish; no future subscription refreshes.

## Part 2 — Admin sidebar reorganization

Current sidebar is one flat list of ~18 entries. Proposed sections:

```
SERVER
  Dashboard
  System Settings              (core, remote access, request servers)
  Background Tasks
  Users & Access
  Authorized Devices
  Watch History
  Music History
  AI Usage & Stats             (only when AI plugin enabled)

LIBRARY
  Libraries                    (incl. Artwork, Metadata, Ratings, FolderWatcher, LocalScanner, Chronology plugins)
  Poster Overlays              (incl. OverlayEngine plugin)
  Media Deduplication
  Smart Lists                  (home-screen layout — clients also use it; admin owns the catalog)
  Collections                  (NEW — admin-managed collection sync + Collection Sync plugins)
  Request Queue                (incl. Request plugins)

FEATURES                       (each toggleable; toggle lives at the top of its page)
  Discover                     (toggle + layout + Discovery plugins + Theater plugin)
  For You                      (toggle + Recommendation plugins + music recommendation cadence)
  Release Calendar             (toggle + Calendar plugins)
  Live TV                      (toggle + playlists + EPG + Match Diagnostics)
  DVR                          (toggle + retention defaults; greyed out when Live TV is off)
  Internet Radio               (toggle + playlists)
  Podcasts                     (toggle + catalog + PodcastDiscovery plugin)
```

Headers (`SERVER`, `LIBRARY`, `FEATURES`) render as the small uppercase tracking-widest text that's already used inside the System Settings sub-sidebar — visual style stays consistent.

### What moves where

- The current "Discover Layout" page becomes the **Discover** feature page.
- The current "IPTV & Live TV" page split into **Live TV** + **DVR** (DVR's settings already exist server-side; it just doesn't have its own admin page today).
- "Internet Radio" stays where Phase 6 put it.
- "Podcasts" stays where it already is, but gains the PodcastDiscovery plugin settings inline.
- "System Settings" loses its **Plugins** sub-page entirely (the in-page sub-sidebar with Artwork/Calendar/Chronology/etc. shown in your screenshot). The plugins distribute to their feature pages instead — see Part 3.
- "Smart Lists" stays under Library (the admin curates them; clients pick which ones show on Home).

### Naming nits

- "System Settings" stays. It's about server-level config (registration mode, scan schedule, transcoding, remote access).
- The "Core Settings" sub-tab inside System Settings becomes just the top of the System Settings page — drop the unnecessary sub-sidebar inside.
- "Music History" stays separate from "Watch History" for now (they're truly different — one is video plays, the other is music plays with scrobbling). Optional later: tab them together under "History".

## Part 3 — Plugins move to feature pages

The System Settings → Plugins screen with its 17-category sub-sidebar goes away. Each plugin category gets a canonical home page. The plugin settings UI itself doesn't change — it just renders inside the relevant feature/library page rather than under one mega-tab.

| Plugin interface | Lives on |
| --- | --- |
| `IArtworkProvider` (Artwork) | Libraries |
| `IMusicArtworkProvider` (also Artwork-tagged) | Libraries (music-specific section) |
| `IMetadataProvider` | Libraries |
| `IRatingsProvider` | Libraries |
| `IFolderWatcherProvider` | Libraries |
| `ILocalMediaScannerProvider` | Libraries |
| `IChronologyProvider` | Libraries (governs viewing-order for movie franchises) |
| `ICollectionSyncProvider` | Collections |
| `IDiscoveryProvider` | Discover |
| `IDiscoveryTheaterProvider` | Discover |
| `IRecommendationProvider` | For You |
| `ICalendarProvider` | Release Calendar |
| `IOverlayProvider` | Poster Overlays |
| `IPodcastDiscoveryProvider` | Podcasts |
| `IRequestProvider` | Request Queue |
| `ILyricsProvider` | Audio / Music section (lives inside the Library page's Music tab, since Music is a library type, not a feature toggle) |
| `IListeningDataProvider` (Last.fm scrobble) | Audio / Music section |

### Plugin Management vs Plugin Settings

Two different things, easy to confuse:

- **Plugin Management** (the existing `/admin/plugins` page where you install / enable / disable / uninstall whole plugin packages) — stays exactly as it is. This is the admin's package manager.
- **Plugin Settings** (the per-plugin config UI — API keys, behavior toggles, etc., currently rendered in the System Settings → Plugins sub-sidebar) — these move out to feature pages.

So the existing `/admin/plugins` page stays untouched. The "Plugins" sub-tab inside `/admin/settings` goes away, and its per-plugin settings render inside the feature page that owns them.

The existing `pluginAdminService.getPlugins()` already returns plugins with a category, so feature pages just filter by the relevant plugin interface. No backend changes for plugin enumeration.

## Part 4 — Anatomy of a feature page

Every feature page has the same skeleton:

```
[Page Title]
[Toggle: Enable <feature>]            ← single switch at the top
[Description: what turning this off does]

─────────
[Feature-specific settings panel]
  (e.g. for Discover: Discover layout editor)
  (for Live TV: playlists table + EPG sources)
  (for For You: music mix cadence)

─────────
[Plugins for this feature]
  Plugin A (enabled checkbox + settings)
  Plugin B (enabled checkbox + settings)
```

When the toggle is off, the rest of the page greys out but remains editable so admins can configure it before flipping the switch on. The toggle commits independently of the rest.

## Decisions locked in

1. Nav gating ties to the active server. If the active server has a feature off, the nav entry doesn't show.
2. Plugin Management page (`/admin/plugins`) stays as-is. Per-plugin settings move to feature pages. No "All Plugins" settings view.
3. Smart Lists stay always-on (no toggle).
4. Collections gets its own admin page with Collection Sync plugins.
5. Feature order: Discover, For You, Release Calendar, Live TV, DVR, Internet Radio, Podcasts.

## DVR page contents

Reviewed the existing DVR code. A few items I initially proposed don't fit Vora's pipeline; I've removed those and shifted the rest.

What's already there:

- `User.DvrStorageQuotaBytes` — per-user quota, enforced by `DvrRecordingService` before starting a recording.
- `IptvRecordingSchedule.KeepMaxEpisodes` — series retention, set by the client when scheduling a series. Persists per-schedule.
- `DvrPostProcessingWorker` — runs every minute, converts every completed raw `.ts` recording to MP4. If `DisableVideoTranscoding` is set, it remuxes (fast, lossless); otherwise it transcodes via the global encoder settings. Original `.ts` is always deleted on success.
- Comskip — when installed, runs automatically after post-processing and writes commercial markers to the session.
- Storage path — read from `IConfiguration["StoragePaths:IptvDvr"]` (default `/app/data/iptv/dvr`), not in the DB today.

So the DVR page additions:

**Storage**
- DVR storage path — promote the config setting to a DB-backed admin setting. Reads default from config on first migration, then admin-editable.
- Server-wide max DVR storage size (GB; 0 = unlimited). Separate from the per-user `DvrStorageQuotaBytes`. Soft cap that warns + refuses to start new recordings when exceeded.
- Storage warning threshold (% full). Drives the notification.
- Auto-delete watched recordings after N days (0 = never). Background sweep.

**Recording defaults**
- Default series retention (the value the client sees pre-filled when scheduling a series; client can still override per-schedule). Backs `KeepMaxEpisodes`.

(Pre/post-roll padding and conflict resolution are net-new scheduler features — split into Phase 6 below.)

**Post-processing**

Most of this is automatic and not worth exposing as toggles; the page surfaces what's happening:

- Read-only line confirming recordings are auto-converted to MP4 using the global transcoding settings (link to System Settings → Transcoder).
- Read-only line confirming Comskip is installed (or not) and that commercial detection runs automatically when it is. If you ever want to skip Comskip even when installed, we can add a toggle here — but right now there's no use case to suppress it.

(No transcode-after-recording toggle; no delete-original toggle. Both are intrinsic to the pipeline.)

**Notifications**
- Notify on recording failure.
- Notify on storage threshold reached.
- Surfaced via the same toast/SignalR pipeline as other admin notifications.

Anything else worth adding before I start Phase 1?

## Phased rollout

Splitting because this touches a lot of files. Each phase is independently shippable.

**Phase 1 — Feature toggles backend**
- Add seven bools to `ServerSetting` (PMC migration: `add-migration AddFeatureToggles`).
- Add `FeatureFlagsVM` + `GET /api/server/features` + admin `PUT /api/server/features`.
- Add a `RequireFeature` filter and apply to each toggleable endpoint group.
- ~3-4 hours.

**Phase 2 — Frontend feature gating**
- New `featureFlagsService` + a context/hook (`useFeatureFlags`) that aggregates flags across connected servers.
- `MainLayout.tsx` filters `baseItems` by flag.
- "Edit Navigation" (Manage Sidebar) hides feature-disabled rows.
- ~3 hours.

**Phase 3 — Admin sidebar restructure (no feature pages yet)**
- Update `AdminLayout.tsx` with the three section headers and re-grouped order.
- No moves yet — just regrouping the existing entries so admins see the new shape early.
- ~1-2 hours.

**Phase 4 — Build feature pages (one at a time)**
- Start with Discover (most plugin-light, easiest pattern to establish).
- Then For You, Release Calendar, Live TV (already partly grouped), DVR, Internet Radio, Podcasts.
- Each: page scaffold + toggle wired to backend + existing settings imported + plugin filter applied.
- Drop the System Settings → Plugins sub-sidebar once all features have absorbed their plugins.
- ~half a day per feature; some are faster.

**Phase 5 — Cleanup**
- Remove dead routes and unused PluginSettingsTab sub-routes.
- Update `docs/architecture.md` and `docs/plugins.md` references.
- Verification: connect two servers with different feature flag combinations, confirm nav gating works correctly.
- ~2 hours.

**Phase 6 — DVR scheduler features (separate)**
- Pre-roll padding: extend the recording start time by the configured number of seconds.
- Post-roll padding: extend the recording end time by the configured number of seconds.
- Conflict resolution policy: when two scheduled recordings overlap and there's no spare tuner, apply the admin's chosen rule (drop oldest / drop newest / always record / cancel both with admin notice). Requires introducing overlap detection in the scheduler.
- Settings UI lives on the DVR page (Recording defaults section).
- ~half a day to a day.

Total: roughly 2-3 days of focused work for Phases 1-5, plus a separate day for Phase 6.

## Files that will be touched

- Backend: `ServerSetting.cs`, a new `FeatureFlagsVM.cs`, `SystemSettingsAdminEndpoints.cs`, `SystemSettingsAdminService.cs`, one new EF migration, the endpoint files for each toggleable feature (`DiscoveryEndpoints`, `RecommendationEndpoints`, `CalendarEndpoints`, `IptvClientEndpoints`, `IptvAdminEndpoints` partial, `DvrEndpoints`, `PodcastEndpoints`).
- Frontend admin: `AdminLayout.tsx` (sidebar), `pages/Admin/SettingsPage.tsx` (drop the Plugins sub-tab), new `pages/Admin/Features/*.tsx` (one per feature), existing pages relocated/renamed.
- Frontend client: `MainLayout.tsx` (gate `baseItems`), `MainLayoutSidebar.tsx` (no change), `AudioHubPage.tsx` (hide tabs for disabled features).
- Services: new `featureFlagsService.ts` + `useFeatureFlags` hook.
- Docs: this plan, `architecture.md`, `plugins.md`.

Decisions are locked in above. Ready to start Phase 1 when you give the word — or push back on the DVR option list first if any of those don't fit.
