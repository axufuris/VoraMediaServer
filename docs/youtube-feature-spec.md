# YouTube Feature Specification

## Overview

Add a YouTube navigation item to the Vora client that gives profiles a cinematic browsing and playback experience for YouTube content. The feature operates entirely without requiring a user to log into YouTube or link a Google account. All personalization (watch history, subscriptions) is stored inside Vora against the profile, not against a Google account.

## Goals

- Let profiles browse trending YouTube content, search YouTube, and subscribe to channels — all from within the Vora client
- Play YouTube videos natively inside the Vora player using the YouTube iframe Player API
- Build a personalized feed over time using Vora-side watch history and channel subscriptions
- Stay fully within YouTube's Terms of Service and require no per-user Google OAuth
- Integrate with Vora's existing parental controls so restricted profiles receive filtered content
- Give admins full control over YouTube availability — globally, per user account, and per profile

## Non-Goals

- Linking a user's existing Google/YouTube account to Vora
- Accessing a user's YouTube watch history from Google's servers
- Downloading or re-streaming video content (all playback goes through the official iframe embed)
- Accessing private or unlisted videos

---

## Feature Visibility & Access Control

YouTube availability follows a three-tier model. Each tier can independently disable the feature, and the most restrictive setting wins. The YouTube nav item and all related pages are hidden from a profile whenever any tier has the feature disabled for that profile.

```
Server Admin (global)
  └── Account-level (per user, set by admin)
        └── Profile-level (per profile, set by user)
```

### Tier 1: Server-Wide Toggle (Admin)

The admin controls a global **YouTube Enabled** toggle in the YouTube plugin settings page in the Vora admin interface. When disabled globally, the YouTube nav item is hidden for every profile on the server regardless of any other settings. This is the master switch — useful for server admins who want to deploy Vora without the YouTube feature at all, or temporarily suspend it.

This toggle is stored as a plugin setting (`youtube_enabled`, boolean, default `false`). The feature is **off by default** and must be explicitly enabled by the admin after configuring the API key.

The admin settings page for the YouTube plugin should therefore expose two fields:
- **YouTube Data API Key** — the Google Cloud API key (secret field)
- **Enable YouTube Feature** — boolean toggle (only activatable once an API key is present)

### Tier 2: Per-User Toggle (Admin-Controlled)

The admin can enable or disable YouTube for individual user accounts. This is managed from the user's account page in the Vora admin interface, under a "Features" or "Access" section alongside other per-user permissions.

When YouTube is disabled at the user level, none of that user's profiles will see the YouTube nav item, regardless of their own profile-level setting. This is the right control for an admin who wants to grant YouTube access to some household members but not others.

The per-user setting has three states:
- **Inherit** (default) — the user follows the server-wide toggle
- **Enabled** — YouTube is available to this user's profiles (subject to profile-level preference), even if the server default were ever turned off
- **Disabled** — YouTube is hidden for all of this user's profiles regardless of server or profile settings

### Tier 3: Per-Profile Toggle (User-Controlled)

Within their own settings, a user can enable or disable the YouTube page for each of their profiles. This allows a user to have YouTube visible on their main profile but hidden on a "Movie Night" or shared profile, for example.

This is a simple boolean the profile owner controls from their profile settings page. It defaults to **enabled** (i.e., the profile inherits whatever the server and account tiers allow). A profile that has YouTube turned off by its own user will simply not show the nav item — no admin action is needed.

### Resolution Chain

When determining whether to show the YouTube nav item for a given profile, resolve in this order:

1. Is the YouTube plugin installed and the API key configured? If not → **hide**.
2. Is the server-wide toggle enabled? If not → **hide**.
3. Is the account-level setting `Disabled`? If so → **hide**.
4. Is the profile-level setting disabled by the user? If so → **hide**.
5. Otherwise → **show**.

The backend should enforce this chain on every YouTube API call, not just at the nav item level. A profile that should not have YouTube access must receive a `403` from any YouTube endpoint, so the feature cannot be accessed by manipulating the frontend.

---

## Authentication Model

There is **no per-user authentication**. The entire feature runs off a single YouTube Data API v3 API key configured by the server admin.

### Admin Setup

The server admin registers a free Google Cloud project, enables the YouTube Data API v3, and generates an API key. This key is entered once in Vora's admin settings under the YouTube plugin. All YouTube API calls made by Vora use this single server-side key.

The API key should be stored as a plugin setting using `IPluginSettingsProvider`. It should be treated as a secret (masked in the admin UI, redacted from logs). It can also be seeded via the `Vora__PluginSettings__youtube__api_key` environment variable using the existing env-seeding mechanism.

### Quota Awareness

The YouTube Data API v3 free tier provides **10,000 units per day** per project. Quota costs:
- Search: 100 units per call
- Video/channel metadata lookups: 1–3 units per call
- Trending list: 1 unit per call

For a personal or small-family server this is generous. The backend should cache API responses aggressively (trending: 30 min, search results: 5 min, channel metadata: 1 hour) to keep quota usage low across multiple profiles.

---

## Core Features

### 1. YouTube Navigation Item

A new top-level nav item appears in the client alongside Home, Library, Live TV, etc. The page is only shown if the server admin has configured a valid API key. If no key is configured, the nav item is hidden.

### 2. YouTube Home Page

The default view when the user lands on the YouTube page. Sections:

- **Continue Watching** — videos the profile has started but not finished (sourced from Vora watch history), shown as a rail
- **From Your Subscriptions** — latest uploads from the profile's Vora-side subscribed channels, ordered by publish date
- **Trending** — fetched from the YouTube Data API trending endpoint (`videos?chart=mostPopular&regionCode=US`), refreshed every 30 minutes
- **Recommended For You** — computed from the profile's watch history (see Recommendations section below)

If the profile has no watch history or subscriptions yet (fresh state), the page shows Trending and a "Search to get started" prompt.

### 3. Search

A search bar is prominently placed on the YouTube page. Typing triggers a call to the backend which proxies to the YouTube Data API `search` endpoint. Results display as a grid of video cards showing thumbnail, title, channel name, and view count.

Search results are not paginated on first implementation — return the top 20 results. Pagination can be added later via `nextPageToken`.

### 4. Video Playback

When a profile selects a video, it opens in the Vora player using the **YouTube iframe Player API**. The iframe is embedded inside Vora's existing player shell. The player should support:
- Play/pause (via iframe postMessage API)
- Full screen
- Returning to browse without losing position (track `currentTime` in local state)

Because playback goes through the official iframe embed, YouTube's standard ad experience is preserved. This is correct behavior and required by YouTube's ToS.

When a video finishes or the user navigates away, Vora records a watch history entry (see Data Model below).

### 5. Vora-Side Channel Subscriptions

Each profile can subscribe to YouTube channels within Vora. This has no connection to the user's actual YouTube account — it is purely a Vora record.

**Subscribe flow:**
- On any video card or channel page, a Subscribe button is present
- Clicking it adds a `YouTubeSubscription` record to the database for that profile + channel ID
- The button toggles to Unsubscribe if already subscribed

**Subscription feed:**
- The backend fetches recent uploads for each subscribed channel using the YouTube Data API (via the channel's uploads playlist ID, which is derivable from the channel ID)
- Results are cached per channel for 15 minutes to avoid hammering the API quota when a profile has many subscriptions
- The "From Your Subscriptions" rail on the home page is built from this feed, sorted newest first

As an optimization (and quota fallback), the YouTube Atom/RSS feed for each channel (`https://www.youtube.com/feeds/videos.xml?channel_id=CHANNEL_ID`) can be used instead of the Data API for subscription feeds. RSS is completely free with no quota. Use RSS for subscription feeds and reserve the Data API quota for search and metadata enrichment.

### 6. Vora-Side Watch History

Every time a profile watches a YouTube video, Vora records:
- YouTube video ID
- Video title and thumbnail URL (stored at time of watch so they remain available offline)
- Channel ID and channel name
- Watch timestamp
- Duration watched (seconds) and total duration (seconds)

This data is stored in the `YouTubeWatchHistory` table scoped to the profile. It never leaves Vora and is not synced to Google.

Watch history drives:
- The **Continue Watching** rail (videos where `durationWatched / totalDuration < 0.9`)
- The **Recommended For You** section (see below)
- The channel subscription feed weighting (channels watched frequently surface first)

### 7. Recommendations

Vora builds a simple recommendation feed from the profile's watch history without any server-side ML. The approach:

1. Take the most recently watched 10–20 video IDs from the profile's history
2. For each, call the YouTube Data API `search` endpoint with `relatedToVideoId` (or use the `videos` endpoint to get tags/category, then search by those)
3. Deduplicate and shuffle, removing videos already in watch history
4. Cache the result for 1 hour per profile

This gives a "related to things you've watched" feed that improves as the profile builds up history. It is not as sophisticated as YouTube's own algorithm but provides a reasonable personalized experience without requiring any account linkage.

---

## Data Model

### `YouTubeAccountSettings`

Stores the admin-controlled per-user toggle. A row is only created when the admin explicitly overrides the default — absence of a row means the account inherits the server-wide setting.

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `AccountId` | `Guid` | FK → `Account`, unique |
| `YouTubeAccess` | `YouTubeAccessSetting` | Enum: `Inherit`, `Enabled`, `Disabled` |
| `UpdatedAt` | `DateTimeOffset` | |

### `YouTubeProfileSettings`

Stores the per-profile user-controlled toggle. A row is created on first save. Absence of a row means the profile follows the account/server tiers.

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserProfileId` | `Guid` | FK → `UserProfile`, unique |
| `IsEnabled` | `bool` | `true` by default (user has opted in) |
| `UpdatedAt` | `DateTimeOffset` | |

### `YouTubeSubscription`

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserProfileId` | `Guid` | FK → `UserProfile` |
| `ChannelId` | `string` | YouTube channel ID (e.g. `UCxxxxxx`) |
| `ChannelName` | `string` | Stored at subscribe time |
| `ChannelThumbnailUrl` | `string?` | Stored at subscribe time |
| `SubscribedAt` | `DateTimeOffset` | |

Unique constraint on `(UserProfileId, ChannelId)`.

### `YouTubeWatchHistory`

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserProfileId` | `Guid` | FK → `UserProfile` |
| `VideoId` | `string` | YouTube video ID |
| `VideoTitle` | `string` | Snapshot at time of watch |
| `ThumbnailUrl` | `string` | Snapshot at time of watch |
| `ChannelId` | `string` | |
| `ChannelName` | `string` | |
| `DurationWatched` | `int` | Seconds |
| `TotalDuration` | `int` | Seconds (0 if unknown) |
| `WatchedAt` | `DateTimeOffset` | |

Index on `(UserProfileId, WatchedAt DESC)` for feed queries.
Index on `(UserProfileId, VideoId)` for deduplication during recommendation building.

---

## Backend API Endpoints

All endpoints are profile-scoped (require a valid profile token).

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/youtube/trending` | Returns trending videos for configured region |
| `GET` | `/api/youtube/search?q={query}` | Proxies search to YouTube Data API |
| `GET` | `/api/youtube/feed` | Returns the home page feed (subscriptions + recommended + trending) |
| `GET` | `/api/youtube/subscriptions` | Returns the profile's Vora-side subscriptions |
| `POST` | `/api/youtube/subscriptions` | Subscribes to a channel |
| `DELETE` | `/api/youtube/subscriptions/{channelId}` | Unsubscribes from a channel |
| `GET` | `/api/youtube/history` | Returns the profile's watch history |
| `POST` | `/api/youtube/history` | Records a watch history entry |
| `DELETE` | `/api/youtube/history` | Clears the profile's watch history |
| `GET` | `/api/youtube/channel/{channelId}` | Returns channel metadata + recent uploads |

Profile-level (user-controlled):
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/youtube/settings` | Returns the current profile's YouTube settings (enabled flag) |
| `PUT` | `/api/youtube/settings` | Updates the current profile's enabled preference |

Admin-only:
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/youtube/status` | Returns API key config status and quota health |
| `GET` | `/api/admin/youtube/accounts/{accountId}` | Returns the YouTube access setting for a user account |
| `PUT` | `/api/admin/youtube/accounts/{accountId}` | Sets the YouTube access setting for a user account (`Inherit`, `Enabled`, `Disabled`) |

---

## Frontend Structure

### Pages

- `YouTubePage.tsx` — main entry, renders the home feed with rails
- `YouTubeSearchPage.tsx` (or inline search results state within `YouTubePage`) — search results grid
- `YouTubeChannelPage.tsx` — channel view with recent uploads and subscribe button
- `YouTubePlayerPage.tsx` — full-screen iframe player with back navigation

### API Service

`src/api/YouTube/youtubeService.ts` — all API calls go through this service. No direct axios calls from pages.

### Shared Components

- `YouTubeVideoCard.tsx` — thumbnail, title, channel name, duration badge, subscribe shortcut
- `YouTubeChannelBadge.tsx` — channel avatar + name, subscribe toggle
- `YouTubePlayerEmbed.tsx` — wraps the YouTube iframe Player API, exposes play/pause/seek via ref

### Design Language

The page should follow the existing cinematic design language. Use `MediaRail` for horizontal scroll rails. Use `var(--vora-*)` tokens for all colors — never Tailwind palette values. The player should sit inside the existing player shell pattern used elsewhere in the client.

---

## Plugin Structure

This feature ships as a Vora plugin (`youtube`) implementing the relevant plugin contracts. The plugin:

- Declares two settings via `IPluginSettingsProvider`:
  - `api_key` (type: secret string, label: "YouTube Data API Key")
  - `youtube_enabled` (type: boolean, label: "Enable YouTube Feature", default: `false`)
- Registers the YouTube navigation item into the client nav (only rendered when the resolution chain resolves to visible for the active profile)
- Provides the backend manager and endpoint registration via the plugin loader
- Adds a "YouTube" section to the user account page in the admin interface for the per-user access toggle
- Adds a "YouTube" toggle to the profile settings page in the client for the per-profile preference

The feature is opt-in at every level: the plugin must be installed, the API key configured, and the global toggle enabled before any profile sees the nav item. Admins can then refine access per user, and users can further refine per profile.

---

## Parental Controls

### What the YouTube API Actually Exposes

The YouTube Data API v3 includes a `contentDetails.contentRating` object on every video resource. It has fields for many rating systems — `mpaaRating` (G, PG, PG-13, R, NC-17), `tvpgRating` (TV-Y, TV-Y7, TV-G, TV-PG, TV-14, TV-MA), `bbfcRating`, `acbRating`, and others.

**The critical limitation:** these fields are almost never populated for user-generated content, which is the majority of YouTube. They only get filled in when a studio or broadcaster explicitly tags officially uploaded content. For general YouTube browsing, treating these fields as a primary parental control will fail silently most of the time — the fields simply come back empty.

There are two signals that are reliable and should be used as the actual control layer.

### Primary Control: `safeSearch`

All YouTube Data API calls that return video lists (search, trending, recommendations) accept a `safeSearch` parameter:

- `strict` — filters videos with explicit content, equivalent to YouTube's own Restricted Mode
- `moderate` — the API default; filters the most explicit content but not everything
- `none` — no filtering

When a profile has parental restrictions enabled in Vora, **all** YouTube API calls from that profile's session must pass `safeSearch=strict`. This is what YouTube itself uses internally for Restricted Mode and is the most reliable filtering signal available.

### Secondary Control: `ytRating`

The one `contentRating` field that is consistently populated is `contentDetails.contentRating.ytRating`. When set to `ytAgeRestricted`, YouTube has explicitly flagged the video as age-gated. These videos should be filtered from results for restricted profiles.

There is also a practical enforcement layer here: age-restricted videos **cannot be embedded via the iframe Player API** without the viewer being signed into Google and old enough. They will fail to play in Vora regardless of filtering. Explicit filtering is still correct — don't surface them on the home page or in search results — but Vora does not need special handling in the player for this case.

### Tertiary Control: `mpaaRating` / `tvpgRating`

Where these fields are populated, they should be compared against the profile's allowed rating ceiling and the video excluded if it exceeds the limit. Map them the same way Vora maps ratings elsewhere in the system.

Because these fields are absent on the vast majority of YouTube content, they function as an extra signal on officially published studio content, not as a general content filter. Do not rely on them alone.

### Integration with Vora Profile Parental Settings

The YouTube plugin should read the existing profile's parental control configuration. No new profile settings are needed specific to YouTube — the existing enabled/disabled flag and rating ceiling should drive the three controls above:

- Parental controls **enabled** on profile → `safeSearch=strict` on all API calls, `ytRating=ytAgeRestricted` videos filtered from all results, `mpaaRating`/`tvpgRating` filtered against allowed ceiling
- Parental controls **disabled** → `safeSearch=moderate` (API default), no rating filtering

The `safeSearch` value should be determined server-side by the backend, based on the profile's settings read from the claims/profile record. The frontend should never be able to override this by manipulating the request.

### New Endpoint Parameter

The `/api/youtube/search`, `/api/youtube/trending`, `/api/youtube/feed`, and `/api/youtube/channel/{channelId}` endpoints should all apply the correct `safeSearch` level automatically based on the authenticated profile. No client-side parameter is needed or accepted — the backend resolves it from the profile.

---

## Terms of Service Compliance Notes

- All video playback goes through the official YouTube iframe Player API. Vora never touches the raw HLS stream.
- All data fetching uses the official YouTube Data API v3 with a server-provided API key.
- Ads are served by the YouTube player as normal. Vora makes no attempt to suppress them.
- No user Google account data is accessed. The OAuth scopes are never requested.
- Watch history and subscriptions are Vora-internal records and are not written back to YouTube's systems.
- The feature does not "substantially replicate" YouTube as a competing service — it is a personal client interface using official APIs, consistent with YouTube's intended API use cases.

---

## Out of Scope (First Implementation)

- Google account OAuth linking
- YouTube Shorts support (iframe works but UX needs separate consideration)
- Offline/download of any YouTube content
- Comments
- Likes/dislikes
- Casting YouTube to another device
- Playlist management on YouTube's side
