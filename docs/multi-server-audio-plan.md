# Audio + cross-server plan

Scope:

1. Confirm Music library + lyrics match the Movies/Shows multi-server pattern.
2. Replace the implicit single-Music-tab assumption with a **server chip-switcher** inside the Music tab, so users with multiple servers carrying music can move between them without leaving the Audio hub.
3. Make the **Podcast browse catalog** aggregate items across all connected servers.
4. Make the **global search bar** in the header fan out across all connected servers and tag results by source.

Out of scope: turning Music into a cross-server library (decision: keep music per-server like Movies/Shows). See discussion notes at the bottom for the reasoning.

## How Movies/Shows do multi-server today

The pattern is **single server at a time, scoped by URL**:

- `serverVault` (`src/utils/serverVault.ts`) holds N servers in `localStorage`; one is marked active.
- `apiClient` (`src/api/client.ts`) accepts `{ serverId }` in every request config. When omitted it falls back to the active server's base URL and token.
- `App.tsx` exposes two route trees: top-level paths target the active server; `/server/:serverId/*` paths scope to a specific server. `ServerContextWrapper` swaps the active server when entering a server-scoped route.
- Pages call `useParams<{ serverId?: string }>()` and thread `serverId` into every service call (see `HomePage.tsx`, `MediaDetailsPage.tsx`, `libraryService.ts`).

There is no cross-server aggregation in Movies/Shows. "Multi-server" means the user views one server at a time, the URL says which one, and switching is one click.

## Music library + lyrics — current state

Verdict: **already at parity** with Movies/Shows. The only UX gap is that the Music tab has no visible per-server switcher today — handled by Phase 1 below.

What's already correct:

- `src/api/Music/musicService.ts` — every method accepts an optional `serverId` and forwards it to `apiClient`. Same shape as `libraryService.ts`.
- `src/pages/Client/Audio/MusicTab.tsx` — reads `serverId` from `useParams` and threads it through every call: browse, mixes, stations, year recap, genres, similar artists, liked tracks, search, history, recently-added. Stream URLs are built from `serverVault.getServer(serverId) ?? getActiveServer()` (lines 180–186, 402, 424, 444).
- `src/components/Player/NowPlayingFullscreen.tsx` — lyrics, like/unlike, save-station, and liked-tracks lookup all use `serverId` from `useParams` (lines 59, 85, 99, 125–126).
- `src/components/Media/MusicMetadataEditModal.tsx` — accepts `serverId` as a prop and forwards on every upload/update/refresh.
- `src/pages/Admin/MusicHistoryPage.tsx`, `src/pages/Profile/AccountSettingsPage.tsx` (Last.fm) — `serverId` from `useParams`.
- `src/pages/Client/Playlists/*` — all three playlist pages read `serverId` from `useParams` and forward to playlist + music services.

`AudioHubPage.tsx` doesn't pass `serverId` as a prop to `<MusicTab />` or `<PodcastsTab />` — each child calls `useParams` itself. That's the same pattern Movies/Shows pages use, so leave it alone for consistency.

Shared edge case (also affects Movies/Shows):

- `src/contexts/PlayerContext.tsx` makes its music API calls without a `serverId`: `updateNowPlaying` (119), `playbackHeartbeat` (129), `playbackStop` (147, 456), `recordPlay` (358, 379), and `extendRadio` resolves `serverId` from `serverVault.getActiveServerId()` at call time (538–541). The video pipeline does the same with `streamingService.pingSession` / `stopSession` (lines 428, 442, 714).
- Effect: if the user starts a track on Server A, then navigates to `/server/B/livetv`, the active server flips to B (via `ServerContextWrapper`) while the track keeps streaming from A. Lyrics, heartbeats, scrobbles, and recordPlay for that track will go to Server B.
- This is the same defect Movies/Shows have. Optional Phase 4 cleans both up together.

## Podcast browse catalog — current state and feasibility

Today: per-server only. `GET /podcasts/catalog` (`PodcastEndpoints.cs:34`) returns `CatalogPodcastVM[]` keyed by `ShowId` (Guid generated on that server). `PodcastsTab.tsx` calls `podcastService.getCatalog(serverId)` once with the URL's `serverId` and shows just that server's curated list.

Cross-server dedupe key is `feedUrl` (canonical RSS URL — identical across servers when they curate the same show). Each server keeps its own `ShowId` and its own `IsSubscribed` flag (per-profile, per-server). The frontend can fan out the existing endpoint and merge — no backend work.

## Global search — current state

`src/components/Layout/SearchBar.tsx` (line 7) reads `serverId` from `useParams` and passes it to `searchService.searchAll(query, serverId)` plus `discoveryService.search(query, serverId)`. Both calls hit a single server. Result rows navigate to `/server/${serverId}/...` paths using the same `serverId`.

To fan out across servers we need to: call `searchAll` per server in parallel, tag each result with its source server, and have every "click" navigate to that result's source-server route. `discoveryService.search` is an external-metadata search and should remain a single call (results aren't bound to a server).

## Plan

### Phase 1 — Music tab server chip-switcher

A. Backend probe endpoint support. The chip-switcher needs to ask each server "do you have any music?" cheaply. Add an optional `[FromQuery] int? limit` to the existing artists endpoint in `Vora.Api/Endpoints/MusicEndpoints.cs`, thread it into `IMusicRepository.GetArtistsAsync`, and apply `.Take(limit.Value)` in the EF query in `Vora.Infrastructure/Persistence/Repositories/MusicRepository.cs`. Three or four lines total. The probe call becomes `getArtists(undefined, s.id, { limit: 1 })`, returning either an empty array or a single-row array — bounded payload, single-row DB query, regardless of library size.

B. New shared piece: a server chip-switcher component. Drop it under `src/components/Audio/` or reuse it inline in `MusicTab.tsx`. Properties:

- Lists every server in `serverVault.getServers()` whose `limit=1` artist probe returned at least one row. Probes run in parallel via `Promise.allSettled`; servers that fail to respond are dropped silently from the chip row.
- When the resulting list has only one server, the chip row hides itself and renders nothing.
- When there are two or more, render a horizontal chip row labeled with each server's `name` from the vault, using the same orange-underline visual treatment as the Audio hub's Radio/Podcasts/Music tabs.
- Clicking a chip navigates via `useNavigate()` to `/server/<id>/audio` (the route already exists in `App.tsx`). `ServerContextWrapper` swaps the active server; `MusicTab` re-reads `useParams` and reloads.
- The active chip is whichever server matches `useParams().serverId` (or active server when the user is on bare `/audio`).

C. Plumbing in `MusicTab.tsx` and the service:

- Extend `musicService.getArtists` (`src/api/Music/musicService.ts` line 268) to accept an optional `{ limit?: number }` and pass it as a query param.
- Render the chip row above the existing search/Library/Stations sub-nav, only when more than one server has music.
- No other change — every existing `musicService.*` call already uses the URL's `serverId` and will follow the chip selection automatically because the navigation rewrites the URL.

D. Persistence:

- Don't add a separate localStorage key. The URL is already the source of truth, and `ServerContextWrapper` keeps `activeServerId` in sync. That means a refresh on `/server/B/audio` lands the user on Server B's music tab; a refresh on `/audio` lands on the active server's music tab. Good enough.

Effort: ~3 hours including the backend probe support.

### Phase 2 — Podcast browse catalog aggregation

A. Extend the client service. In `src/api/Podcasts/podcastService.ts`:

- Add a new VM `AggregatedCatalogPodcastVM` with `feedUrl`, `title`, `author`, `description`, `artworkUrl`, `homepageUrl`, plus `availableOn: { serverId: string; serverName: string; showId: string; isSubscribed: boolean }[]`.
- Add `getAggregatedCatalog(): Promise<AggregatedCatalogPodcastVM[]>` that reads `serverVault.getServers()`, calls `getCatalog(s.id)` for each via `Promise.allSettled` (one slow/offline server must not break the page), merges by lowercased `feedUrl`, picks the first non-empty artwork/description seen, and sorts alphabetically by title. Return a sidecar `{ items, failedServerIds }` so the UI can show a partial-results banner.

B. Rewire the Browse Catalog tab. In `src/pages/Client/Audio/PodcastsTab.tsx`:

- Replace `loadCatalog` (lines 60–70) with the aggregated call.
- Render the merged list. Show a small chip row beneath each title listing the server names that carry it, only when `availableOn.length > 1`.
- Subscribe button:
  - One server: behave as today.
  - Multiple servers: default the target to the currently active server when it's in `availableOn`, otherwise the first entry. Show a "Subscribe on ▾" dropdown listing each server with its current `isSubscribed` (disable already-subscribed entries).
  - On success, mutate the local `availableOn[i].isSubscribed = true` for the target.
- The `PodcastEpisodesUpdated` SignalR handler (line 213) calls `getAggregatedCatalog()` to refresh.

C. Empty / failure states:

- All servers empty: existing empty-state copy.
- Some servers failed: quiet inline banner "Couldn't reach N server(s) — showing partial results."

D. Admin page (`PodcastsAdminPage.tsx`) stays per-server — it edits one server's curation list. No change.

Effort: ~half a day.

### Phase 3 — Cross-server global search

A. New aggregated client method. In `src/api/Discovery/searchService.ts`:

- Extend each result type with `serverId: string` and `serverName: string` (only for the merged path — preserve the existing `GlobalSearchResponse` shape for any callers that still want a single-server response).
- Add `searchAllServers(query): Promise<AggregatedGlobalSearchResponse>` that fans out `searchAll(query, s.id)` across `serverVault.getServers()` via `Promise.allSettled`, tags every result row with its source server, concatenates each section (movies, tvShows, actors, collections, music), de-duplicates identical-title same-type rows only when both carry the same `id` *and* `serverId` (defensive — they shouldn't collide), and returns the merged sections plus `failedServerIds`.
- Discovery search stays a single call. It's external metadata; results don't have a server.

B. Rewire `SearchBar.tsx`:

- Replace `searchService.searchAll(query, serverId)` (line 29) with `searchAllServers(query)`.
- In each `DropdownItem` `onClick`, build the path using the result's own `serverId`, not the URL's: `/server/${result.serverId}/media/${id}`, `/server/${result.serverId}/actor/${id}`, etc. For collections and the music navigation helper (`navigateToMusic`, lines 87–99), do the same — `navigateToMusic` should accept `serverId` and use it instead of the URL's.
- When more than one server is connected, append the server name to the result subtitle: `"2024 • Movie • Vora Home"`. When only one server is connected, omit it so the UI looks identical to today.
- "View More Results" goes to `/search?q=...` (no server prefix) — the search page itself should also use the aggregated search.

C. Update `src/pages/Client/SearchPage.tsx` the same way: use `searchAllServers`, render results grouped by section, label each row with its server name when N > 1.

D. Failures: same quiet banner pattern as the catalog ("Couldn't reach N server(s)…").

Effort: ~half a day for `SearchBar` and `SearchPage` together.

### Phase 4 — verification

- Two-server smoke test:
  - Music chip-switcher appears, hides on single-server, and clicking a chip navigates between `/server/A/audio` and `/server/B/audio` while preserving the Music sub-tab.
  - Browse Catalog merges shows by feed URL; subscribe target picker works; partial-results banner appears when one server is down.
  - Global search returns results from both servers, each tagged with its server name; clicking each navigates to the correct server's route.
- Single-server regression: chip-switcher absent, catalog identical to today, search subtitles omit server names.
- Offline-server resilience: shut down one server, reload. All three surfaces degrade gracefully.

### Phase 5 — optional: fix the shared player serverId edge case

Only do this if you want to fix the Music + Movies/Shows defect together.

1. Add `serverId?: string` to `PlayableMedia` in `src/contexts/PlayerContext.tsx` (line 9).
2. Every call site that constructs a `PlayableMedia` already has `serverId` in scope. Add it to those literals.
3. Inside `PlayerContext`, replace the bare music calls (`updateNowPlaying`, `playbackHeartbeat`, `playbackStop`, `recordPlay`) with `currentMedia.serverId` instead of relying on the active server. Same for `streamingService.pingSession` / `stopSession` — use `currentMedia.serverId` rather than `serverVault.getActiveServerId()`. The radio extension path (538–541) should pull `serverId` off the media, not the vault.

Effort: ~2 hours. Verify by playing a track on Server A, navigating to `/server/B/...`, and confirming the Network tab still shows heartbeats/lyrics hitting Server A.

## Why Music stays per-server (not aggregated)

Recorded so we don't re-litigate later. Music has too much per-server state to aggregate cleanly:

- `TrackPlayHistory`, `TrackLike`, mixes (`GeneratedMix`), stations (`Station`), and year recap all live on a single server. The recommendation engine seeds from one server's history.
- Last.fm scrobble auth is per-profile-per-server.
- Same album/track on two servers is genuinely two different DB rows with their own play counts, likes, locked fields, and custom artwork.

Cross-server affordances we *do* want — global search (Phase 3) and the Music chip-switcher (Phase 1) — give users a way to reach every server's music without merging the underlying data.

## Files referenced

- `src/api/client.ts`, `src/utils/serverVault.ts`, `src/App.tsx`
- `src/api/Music/musicService.ts`, `src/api/Podcasts/podcastService.ts`, `src/api/Media/libraryService.ts`, `src/api/Discovery/searchService.ts`
- `src/pages/Client/Audio/AudioHubPage.tsx`, `MusicTab.tsx`, `PodcastsTab.tsx`
- `src/components/Player/NowPlayingFullscreen.tsx`, `src/contexts/PlayerContext.tsx`
- `src/components/Layout/SearchBar.tsx`, `src/pages/Client/SearchPage.tsx`
- `src/pages/Client/HomePage.tsx`, `src/pages/Client/Playlists/PlaylistDetailsPage.tsx`
- `src/pages/Admin/Podcasts/PodcastsAdminPage.tsx`
- `Vora.Api/Endpoints/PodcastEndpoints.cs`, `Vora.Api/Endpoints/MusicEndpoints.cs`
- `Vora.Application/Podcasts/ViewModels/CatalogPodcastVM.cs`
