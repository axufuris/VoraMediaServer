# YouTube

A first-party client surface for browsing, searching, and watching YouTube content from inside Vora. Playback goes through the official YouTube iframe Player API. All personalisation (channel subscriptions, watch history, recommendations) is stored against the Vora profile, not against a Google account — the feature requires no per-user OAuth and uses a single server-wide YouTube Data API v3 key configured by the admin.

The original feature specification lives in [`youtube-feature-spec.md`](youtube-feature-spec.md). This doc describes the system as built.

## Plugin

The feature ships as a built-in plugin (`Vora.Plugins/Providers/YouTube/YouTubePlugin.cs`, id `youtube`, type `YouTube`). The loader picks it up automatically — no manual DI. It declares two settings via `GetSettingDefinitions()`:

| Key | Type | Default | Notes |
| --- | --- | --- | --- |
| `api_key` | password | — | Server-wide Google Cloud API key with the YouTube Data API v3 enabled. Treated as a secret (masked in admin UI, redacted from logs). |
| `trending_region` | text | `US` | ISO 3166-1 country code used for the Trending rail. |

The **master switch is the standard plugin `is_enabled` toggle** that the plugin system renders on every plugin card (top-right of the `PluginSection`). We deliberately do *not* declare a separate "Enable YouTube Feature" setting — that would duplicate the standard toggle and confuse admins. Treat absent / non-`"false"` values as enabled (PluginSection convention); only an explicit `"false"` disables.

Both `api_key` and `trending_region` can be seeded from environment variables — `Vora__PluginSettings__youtube__api_key`, `Vora__PluginSettings__youtube__is_enabled`, etc. See the README's *Bootstrapping plugin API keys from environment variables* section.

## Three-tier access control

Visibility resolves through a 5-step chain (`YouTubeAccessResolver`):

1. Plugin installed?
2. API key configured?
3. Server-wide toggle (the plugin's `is_enabled`) on?
4. Account-level setting not `Disabled`?
5. Profile-level setting not explicitly off?

If any tier denies, the YouTube nav item is hidden and every YouTube endpoint returns `403`. The resolver runs on every API call via `YouTubeManager.EnsureAccessAsync` — never trust the frontend alone.

- **Tier 1 (server)** — the plugin's standard `is_enabled` toggle.
- **Tier 2 (per account)** — `YouTubeAccountSettings.YouTubeAccess` (`Inherit` / `Enabled` / `Disabled`). Set by the admin from the "Features" tab in `UserAccessModal`. Absence of a row = `Inherit`.
- **Tier 3 (per profile)** — `YouTubeProfileSettings.IsEnabled` (default `true`). Set by the profile owner in the client `SettingsPage` → Account tab.

## Data model

Four tables live under `Vora.Domain.Entities.YouTube`, configured by `VoraDbContext.ConfigureYouTube`:

| Table | Purpose | Notable indexes |
| --- | --- | --- |
| `YouTubeAccountSettings` | Per-account override of the master toggle | unique `(AccountId)`; `YouTubeAccess` stored as string |
| `YouTubeProfileSettings` | Per-profile opt-in flag | unique `(UserProfileId)` |
| `YouTubeSubscription` | Vora-side channel subscriptions | unique `(UserProfileId, ChannelId)`, plus `(UserProfileId)` |
| `YouTubeWatchHistory` | Per-profile snapshot of every watch event | `(UserProfileId, WatchedAt)`, `(UserProfileId, VideoId)` |

Watch history rows snapshot `VideoTitle`, `ThumbnailUrl`, `ChannelId`, `ChannelName` at the moment of the watch so Continue Watching keeps rendering even if YouTube later removes the video.

## Parental controls

The feature reuses each profile's existing parental settings. `YouTubeAccessResolver` derives `YouTubeAccessResolution` with three controls:

- `SafeSearch` — `Strict` when the profile has any rating restriction or `BlockUnratedContent`; `Moderate` otherwise. Passed on every Data API call (`search`, `videos?chart=mostPopular`).
- `FilterAgeRestricted` — drops videos where `contentDetails.contentRating.ytRating == "ytAgeRestricted"`.
- Rating ceiling — when the profile has `AllowedMovieRatings`/`AllowedTvRatings`, videos whose `mpaaRating` or `tvpgRating` exceeds the ceiling are filtered. (Most user-generated YouTube content has these fields empty; they function as an extra signal on officially published studio content, not a general filter.)

All three apply on every list-returning endpoint. Frontend cannot override safeSearch — the level is resolved server-side from the profile.

## API client and caching

`Vora.Application/YouTube/YouTubeDataApiClient.cs` wraps the YouTube Data API and the per-channel Atom/RSS feed. Two named `IHttpClientFactory` clients:

- `YouTubeDataApiClient.DataApiHttpClientName` (`YouTubeDataApi`) → `https://www.googleapis.com/youtube/v3/`
- `YouTubeDataApiClient.RssHttpClientName` (`YouTubeRss`) → `https://www.youtube.com/feeds/videos.xml`

Per-endpoint TTLs (in `IMemoryCache`):

| Endpoint | TTL | Reason |
| --- | --- | --- |
| Trending | 30 min | Cheap (1 unit), shared across profiles in the same region |
| Search (per query/page-token) | 5 min | Expensive (100 units), but each query is per-profile |
| Channel metadata | 1 hour | Cheap (1–3 units), rarely changes |
| Channel recent uploads (RSS) | 15 min | Free — RSS has no quota |
| Recommendations | 1 hour | Per-profile, expensive (100 units × seed size) |
| Videos by ID (chunks of 50) | 1 hour | Used to enrich search-result IDs into full video records |

**Subscription feeds use RSS, not the Data API.** YouTube's per-channel Atom feed (`feeds/videos.xml?channel_id=…`) is free, has no quota, and returns the last 15 uploads. The Data API quota is reserved for trending, search, channel metadata, recommendations, and video metadata enrichment.

## Endpoints

All profile-scoped endpoints sit at `/api/youtube/...` (`RequireAuthorization()`), admin endpoints at `/api/admin/youtube/...` (`AdminOnly`). Every endpoint enforces the resolution chain via `EnsureAccessAsync`, which throws `UnauthorizedAccessException` on denial — the endpoint catches it and returns `403`.

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/api/youtube/feed` | Home page composite: Continue Watching, From Your Subscriptions, Trending, Recommended For You |
| `GET` | `/api/youtube/trending` | Trending only, region from `trending_region` |
| `GET` | `/api/youtube/search?q=&pageToken=` | Returns `YouTubeSearchPageVM` with `videos` + optional `nextPageToken` |
| `GET` | `/api/youtube/channel/{channelId}` | Channel metadata + RSS-sourced recent uploads, plus `isSubscribed` for the calling profile |
| `GET` | `/api/youtube/video/{videoId}` | Single-video metadata (used by the player page) |
| `GET` | `/api/youtube/subscriptions` | The profile's Vora-side subscriptions |
| `POST` | `/api/youtube/subscriptions` | Body `{ channelId }`; resolves the channel via the Data API and persists name + thumbnail |
| `DELETE` | `/api/youtube/subscriptions/{channelId}` | |
| `GET` | `/api/youtube/history` | Up to 100 most-recent watch records |
| `POST` | `/api/youtube/history` | Records a watch event |
| `DELETE` | `/api/youtube/history` | Clears the profile's history (`ExecuteDeleteAsync`) |
| `GET` | `/api/youtube/settings` | `{ isEnabled, isAvailable, unavailableReason? }` — `isAvailable` reflects the full resolution chain |
| `PUT` | `/api/youtube/settings` | Body `{ isEnabled }` |
| `GET` | `/api/admin/youtube/status` | Admin dashboard: plugin-installed / key-configured / server-enabled flags, trending region |
| `GET` | `/api/admin/youtube/accounts/{accountId}` | Tri-state per-account setting |
| `PUT` | `/api/admin/youtube/accounts/{accountId}` | Body `{ youTubeAccess: 'Inherit' \| 'Enabled' \| 'Disabled' }` — fires `YouTubeAccessChanged` SignalR event |

## Recommendations

Built from the profile's watch history without server-side ML (`YouTubeManager.BuildRecommendationsAsync`):

1. Take the 15 most-recently-watched videos.
2. For each, call `GetRelatedVideosAsync` — the Data API's true `relatedToVideoId` parameter is deprecated, so this issues a `search` keyed on the source video's title.
3. Aggregate, apply rating filters, then dedupe against: the watched-ID set, the home page's trending and subscription-feed video IDs, and the set of channels the profile is already subscribed to (so Recommended doesn't duplicate signal already carried by the other rails).
4. Shuffle, take 25.

## Real-time

When an admin changes per-account access, `YouTubeManager.UpdateAccountSettingsAsync` fires `IClientNotifier.NotifyYouTubeAccessChangedAsync(userId)`. The SignalR event name is `YouTubeAccessChanged` with the affected user's id as payload. `MainLayout` and the `YouTubeToggleSection` on the client settings page both subscribe via `useSignalREvent` and re-fetch `youtubeService.getProfileSettings()` when the changed user matches the current one — the nav item appears or disappears live without a profile re-select.

## Frontend

Routes:

- `/youtube` → `YouTubePage` — search + home rails. Mirrored under `/server/:serverId/youtube`.
- `/youtube/channel/:channelId` → `YouTubeChannelPage` — header, subscribe toggle, recent uploads grid.
- `/youtube/watch/:videoId` → `YouTubePlayerPage` — iframe player + metadata. On unmount it POSTs the final `currentTime` / `duration` so Continue Watching populates.
- `/admin/youtube` → `YouTubeAdminPage` — status card, `FeaturePluginList pluginTypes={['YouTube']}` to render the plugin's three settings, parental + quota info card. Listed in `adminNavData.tsx` under the *Features* section.

Files:

- API service: `src/api/YouTube/youtubeService.ts` — all calls route through `apiClient` with `{ serverId }` support.
- Components: `components/YouTube/YouTubeVideoCard.tsx` (wraps `MediaStill`, 16:9, duration badge, view-count formatting), `YouTubeChannelBadge.tsx`, `YouTubePlayerEmbed.tsx` (lazy-loads `https://www.youtube.com/iframe_api` once, exposes play/pause/seek via `useImperativeHandle`, fires a 5-second progress callback).
- Nav: `MainLayout` adds a `youtube` base item and gates it via `isNavItemEnabled(item, flags, youtubeAvailable)` — `youtubeAvailable` comes from `getProfileSettings()` plus the live `YouTubeAccessChanged` listener.
- Per-profile toggle: `YouTubeToggleSection` inside the SettingsPage Account tab.
- Per-account admin toggle: `YouTubeAccessControl` inside the `Features` tab of `UserAccessModal`.

## Things to be careful about

- **Age-restricted videos.** YouTube refuses to embed them via the iframe Player API without a signed-in age-verified Google session. We still filter them from results (`ytAgeRestricted`) but no special handling is needed in the player — they just won't play.
- **Quota.** The free tier is 10,000 units/day per project. Search costs 100 units per call, so 100 distinct searches a day exhausts the quota. Cache TTLs are tuned around this; do not drop them.
- **`relatedToVideoId` is gone.** The original spec called for it; YouTube deprecated it. The recommender now searches by source-video title. If you ever wire a richer fallback (e.g. tags + category from `videos?part=snippet,topicDetails`), update `GetRelatedVideosAsync` in `YouTubeDataApiClient`.
- **Subscription feed quota.** Use RSS, not the Data API. `GetChannelRecentUploadsAsync` already does this — don't "optimise" it back to the Data API.
- **Watch history is Vora-internal.** Don't write back to YouTube. The ToS section in `youtube-feature-spec.md` explains why.
