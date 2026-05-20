# ADR-0001: Split IPTV Playlist from EPG Source

**Status:** Accepted (shipped 2026-05-11)
**Date:** 2026-05-11
**Deciders:** Andy (project owner)

## Context

`Vora.Domain.Entities.Iptv.IptvProvider` currently couples two unrelated concerns onto one record: an M3U playlist URL (`M3uUrl`) that defines which channels exist, and an XMLTV URL (`XmlTvUrl`) that supplies the programme guide. `IptvChannel` rows are owned by a single provider via `ProviderId`. EPG sync in `IptvEpgService.SyncProviderAsync` fetches each provider's `XmlTvUrl` and calls `XmlTvParser.ParseAsync(stream, providerDbChannels, ...)`, scoping the parsed programmes to channels whose `ProviderId == provider.Id`.

Two problems follow from this shape:

1. **Functional bug.** Users can add EPG-only "providers" (rows with `M3uUrl == null`, `XmlTvUrl != null`) intending to enrich another playlist's channels. Because such rows own zero `IptvChannel`s, the parser receives an empty channel list and discards every programme it parses. The visible symptom is that adding the IPTV Org US M3U and five complementary XMLTV bundles still leaves the guide sparse — the additional bundles never reach any channel.
2. **Modeling mismatch.** The real relationship between playlists and EPG feeds is many-to-many. A single XMLTV bundle (e.g. `epg_ripper_ALL_SOURCES1.xml.gz`) carries `tvg-id`s drawn from many playlists, and any one playlist typically benefits from several XMLTV feeds layered on top of each other (locals + sports + national). Encoding this as a 1:1 field on `IptvProvider` forces users into workarounds (empty-M3U placeholders) that the system then fails to honour.

The EPG cache is already keyed by `ExternalChannelId` (the `tvg-id`), so the in-memory shape is well-suited to a global, playlist-agnostic merge — only the sync logic and entity layout need to change.

## Decision

Split `IptvProvider` into two independent aggregates and make EPG matching global, keyed by `tvg-id`.

- **`IptvPlaylist`** owns channels and tuner concerns. Fields: `Name`, `M3uUrl`, `SupportsWebPlayback`, `MaxConcurrentStreams`, `IsActive`, `LastError`, `TunerProfile`, `Channels`. This is the rename of today's `IptvProvider` with `XmlTvUrl` removed. Access control (`AllowedIptvProviderIds`, `HasAllIptvAccess`) is renamed but otherwise unchanged in semantics.
- **`IptvEpgSource`** is a standalone aggregate. Fields: `Id`, `Name`, `XmlTvUrl`, `Priority` (int, lower = higher priority), `IsActive`, `LastError`, `LastSyncedAt`. No FK to playlist. No channels.

EPG sync iterates every active `IptvEpgSource` and merges parsed programmes into a single cache keyed by `tvg-id`. Conflict resolution is **merge by `(tvg-id, startTime)` with deduplication**; `Priority` is stored now but not yet consulted (left as the layered enhancement). Channels from any playlist automatically pick up programmes by `tvg-id`, with no per-playlist scoping in the parser.

## Options Considered

### Option A: Keep `IptvProvider` as the only entity, fix the parser to match by `tvg-id` globally

| Dimension | Assessment |
|-----------|------------|
| Complexity | Low |
| Cost | Lowest |
| Scalability | Adequate |
| Team familiarity | Highest (no rename) |

**Pros:** Smallest diff. No migration of access-control claims. Frontend mostly unchanged.
**Cons:** The model still says "this EPG belongs to that M3U" — a lie. Users continue creating empty-M3U placeholder rows as a workaround. `IptvProvider` carries a `MaxConcurrentStreams` field that makes no sense for EPG-only rows, and the "Manage Channels" affordance is conditionally hidden based on whether `M3uUrl` is set. The mental model never gets cleaner.

### Option B: One playlist, many EPG URLs (1:N child table)

Add an `IptvProviderEpgUrls` table with `(ProviderId, XmlTvUrl, Priority)` and remove per-provider channel scoping in the parser.

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium |
| Cost | Medium |
| Scalability | Adequate for most cases |
| Team familiarity | Medium |

**Pros:** Fixes the bug. Preserves the existing `IptvProvider` name and access claims.
**Cons:** Still wrong directionally — a shared EPG bundle (e.g. `ALL_SOURCES1`) doesn't naturally belong under any single playlist. Users either pick an arbitrary playlist to attach it to or fall back to placeholder playlists. Doesn't solve the "EPG-only" UX confusion.

### Option C: Full split into `IptvPlaylist` and `IptvEpgSource` (recommended)

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium-High |
| Cost | Highest of the three (one-time) |
| Scalability | Best — independent lifecycle for each concern |
| Team familiarity | Medium (rename + new entity) |

**Pros:** Model matches reality. The admin page can present two distinct sections with affordances appropriate to each. Adding a new EPG bundle is a one-row insert that immediately benefits every playlist. Removing a playlist no longer risks orphaning EPG data (and vice versa). Conflict resolution policy lives in one obvious place.
**Cons:** Larger diff. Migration of existing rows. Rename of access-control fields ripples through `User`, `UserProfile`, claims, VMs, and the frontend. New endpoint group for EPG sources.

## Trade-off Analysis

Option A is rejected because the user has explicitly stated they want the right, efficient, performant solution rather than the lightest-touch fix. Option A keeps the bug-prone mental model.

Option B vs C is the real question. B is a 1:N variant of A — it fixes the bug but keeps the ownership lie. The decisive factor is that the most useful EPG sources in practice (the `epg_ripper_*` bundles in the existing `FREE_PROVIDERS` list) cross playlist boundaries and have no natural parent. Forcing a parent FK on them means either picking an arbitrary playlist (confusing for the admin) or maintaining placeholder-playlist rows (which is exactly the workaround that produced the original bug). Option C eliminates that whole category of confusion.

The migration cost in C is real but bounded: one EF migration, a rename pass over a known set of files (15 files reference `AllowedIptv` / `HasAllIptvAccess` per grep), and a single page split in the admin UI. The cost is paid once.

On conflict resolution, merge-by-`(tvg-id, startTime)` is chosen over priority-wins because:

- It is symmetric in source ordering — admins don't need to reason about precedence to get useful behaviour.
- It naturally tolerates two feeds that cover the same channel but at different times of day (one has primetime, one has overnight).
- Programmes with the same start time are rare collisions; deduping on start time is sufficient.

`Priority` is stored but unused initially so the schema doesn't need to change later when priority-wins becomes a layered option.

## Consequences

What becomes easier:

- Adding a new EPG bundle is a single admin action with no ambiguity about where it "belongs."
- Removing a playlist no longer raises the question "what about its EPG data?" — they're decoupled.
- `IptvPlaylist` carries only fields meaningful for channel sources (`MaxConcurrentStreams`, `SupportsWebPlayback`, `TunerProfile`).
- `IptvEpgSource` is small enough that bulk-importing curated EPG bundles (e.g. a "Quick Add" set) is trivial.

What becomes harder:

- One-time migration touches user/profile access claims and a localStorage-adjacent settings page. The `is_server_admin` precedent shows the project takes claim renames seriously.
- The cache invalidation surface grows slightly: refreshing one `IptvEpgSource` requires merging into the existing cache without dropping contributions from sibling sources. A full re-sync remains the simpler path.

What we'll need to revisit:

- If cache size becomes a concern, the parser can be made to filter against the union of `IptvChannel.ExternalChannelId`s across all playlists rather than storing every `tvg-id` it sees. Defer until measurable.
- Priority-wins conflict resolution can be enabled per-source once a real conflict is observed.
- If/when an `Update Provider` admin action is added, it would split into `Update Playlist` and `Update EPG Source`.

## Action Items

Backend domain & persistence:

1. [ ] Rename `IptvProvider` entity to `IptvPlaylist`; remove `XmlTvUrl`.
2. [ ] Add `IptvEpgSource` entity with `Id`, `Name`, `XmlTvUrl`, `Priority`, `IsActive`, `LastError`, `LastSyncedAt`.
3. [ ] Update `IptvChannel.Provider` / `ProviderId` → `Playlist` / `PlaylistId`. Keep the FK shape (no data move for channels).
4. [ ] Rename `User.AllowedIptvProviderIds` → `AllowedIptvPlaylistIds` and the matching profile field. Same for `HasAllIptvAccess` if a directionally clearer name fits.
5. [ ] Add EF migration `SplitIptvPlaylistFromEpgSource` that: creates `IptvEpgSources`, copies one row from each existing `IptvProvider` with a non-null `XmlTvUrl` into it (carrying the provider's `Name`), drops `XmlTvUrl` from `IptvProviders`, renames `IptvProviders` → `IptvPlaylists`, and deletes ex-providers that had no `M3uUrl` once their EPG row is copied.
6. [ ] Update `ZenithDbContext`, `IptvRepository`, and `IIptvRepository` (currently lists `GetActiveProvidersAsync`, `GetProviderByIdAsync`, etc.) — split into playlist and EPG-source repos or keep one repo with both surfaces; either is fine but choose consistently.

Backend application & API:

7. [ ] Refactor `IptvEpgService.SyncEpgDataAsync` to iterate `IptvEpgSource`s instead of providers. Stop passing `providerDbChannels` into `XmlTvParser.ParseAsync` — parse globally by `tvg-id`.
8. [ ] Implement merge: parsed programmes are inserted into a working dictionary keyed by `tvg-id`; on collision, append and dedupe by `startTime`.
9. [ ] Add `IptvEpgSourceVM` and adjust `IptvProviderVM` → `IptvPlaylistVM` (drop `XmlTvUrl`).
10. [ ] Split `IptvAdminEndpoints` so `/api/iptv/admin/playlists/...` and `/api/iptv/admin/epg-sources/...` are distinct groups; preserve thin-endpoint discipline.
11. [ ] Add `iptvEpgAdminService.ts` alongside `iptvAdminService.ts` for the new endpoint group.

Frontend:

12. [ ] In `IptvPage.tsx`, render two stacked sections: "Playlists" (M3U + tuner settings, "Manage Channels") and "EPG Sources" (XMLTV URL, priority, last-synced, refresh, delete).
13. [ ] Split the `FREE_PROVIDERS` quick-add list into two: playlists (entries with `m3u`) and EPG bundles (entries with `xml` only).
14. [ ] Update `UserAccessModal`, `AccountSettingsPage`, `UserManagementPage` references from `AllowedIptvProviderIds` to `AllowedIptvPlaylistIds`.

Verification:

15. [ ] After migration, confirm the existing IPTV Org (US) playlist's 1351 channels show populated guide data from the previously-orphaned EPG bundles (the original repro for this ADR).
16. [ ] Add an integration test that creates a playlist + two EPG sources covering overlapping `tvg-id`s and asserts the merged cache contains programmes from both, deduped on `startTime`.
17. [ ] Manually verify the access-control rename: a non-admin user with limited playlist access still sees only their allowed channels in the guide.

## Implementation notes (post-merge)

Things that changed between the proposal and what shipped:

- **Conflict resolution swapped to priority-wins per channel.** The proposed `merge by (tvg-id, startTime) with dedup` approach was tried first and failed in practice — feeds covering the same channel publish programmes whose start times drift by seconds or minutes, so equality-based dedup let everything stack. Real-world testing on Comedy Central exposed this immediately. The actual implementation: sources are iterated in ascending `Priority` order; the first source to provide programmes for a given `tvg-id` claims that channel exclusively; lower-priority sources only fill in channels the higher-priority sources didn't cover. Within a single source, programmes are still deduped by `startTime` as a safety net. This collapses to the proposed behaviour when only one source covers a given channel.
- **`tvg-id` matching ended up multi-layered.** Strict equality dropped almost everything because curated M3U and XMLTV authors disagree on punctuation. The final matcher indexes each known channel under four keys: (1) raw `ExternalChannelId`, (2) alphanumeric-only normalised form, (3) the portion before any `@` quality suffix (the iptv-org convention, e.g. `FastTV.us@SD` → `FastTV.us`), and (4) the normalised form of (3). All lookups are case-insensitive. The matcher does **not** strip semantic words (`hd`/`tv`/`east`/etc.) — an earlier attempt at that produced massive false-positive collisions (every `Comedy *` channel collapsing to `comedy`). One M3U-side ID can map to many canonical channels (a single XMLTV programme is fanned out into multiple cache buckets when a playlist carries the same channel in multiple quality variants).
- **Match Diagnostics modal added.** `/api/iptv/admin/epg-diagnostics` returns DB channel samples, channel-coverage summary (matched vs total + uncovered samples), and per-source stats (channels contributed, programmes matched/total, sample unmatched XMLTV IDs). Rendered in `IptvPlaylistEditModal`'s sibling `IptvEpgDiagnosticsModal`. This wasn't in the original ADR but proved essential during onboarding new EPG sources.
- **XMLTV parser allows DTDs.** `XmlReaderSettings.DtdProcessing = Ignore`, `XmlResolver = null`. Real-world feeds (notably `i.mjh.nz/*`) declare a DOCTYPE that .NET's default `Prohibit` mode rejects.
- **Add/Update/Delete on `IptvEpgSource` awaits the sync.** The proposal didn't specify the async vs fire-and-forget choice. We initially used `Task.Run(...)` so the API returned fast, but the UI couldn't observe sync completion (`LastSyncedAt` stayed `null` until the user manually refreshed). Now those three mutations await `SyncEpgDataAsync` before returning so the response carries the fresh `LastSyncedAt`.
- **`IptvPlaylist.LastSyncedAt`** was added later as a parallel to `IptvEpgSource.LastSyncedAt`. Set on successful M3U fetch in `SyncM3uChannelsAsync`; preserves the previous value on failure so a stale `LastSyncedAt` + non-null `LastError` correctly reads as "last good sync was at X, broken since."
- **Migration was hand-written initially, then thrown away.** The original migration was generated by Claude rather than through `Add-Migration` in PMC. The user wiped it and rebuilt an Initial migration from a clean slate. Project rule going forward: don't hand-write EF migrations — let the user generate them via `Add-Migration` in Visual Studio's Package Manager Console.
