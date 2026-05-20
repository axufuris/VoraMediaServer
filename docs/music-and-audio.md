# Music & audio

How the music subsystem is organized — domain model, recommendation engine, playback, and the audio-only surfaces (Music tab, stations, podcasts).

## Domain model

Music sits inside the regular `MediaItem` TPH hierarchy:

- **`Artist`** — `LockableEntity`. Belongs to a `MediaLibrary`. Has `Name`, `SortName`, `Biography`, plus artwork (`ArtworkUrl`, `BackgroundUrl`, `BannerUrl`, `ClearLogoUrl`).
- **`Album`** — `LockableEntity`. FK to `Artist` and `MediaLibrary`. Has `Title`, `Year`, `Genre` (single string, not a join), `AlbumArtist`, `IsCompilation`, plus artwork (`ArtworkUrl`, `BackgroundUrl`, `DiscArtUrl`).
- **`Track : MediaItem`** — FK to `Album`. Has `TrackNumber`, `DiscNumber`, `DurationSeconds`, `Artist` string (per-track, used for compilations), audio metadata (`AudioCodec`, `SampleRate`, `Bitrate`), `HasEmbeddedLyrics`, `ExternalLyricsPath`.
- **`TrackLike`** — per-profile heart.
- **`TrackPlayHistory`** — every play that crosses the Spotify "long enough to count" threshold (≥30s or ≥50% of track). Drives Recently Played, Top Tracks, On Repeat heuristics, recommendation seeding, scrobbles.

Music libraries are filtered out of the left nav (they're surfaced via the Music tab instead).

## Access filtering

`MusicAccessFilter` (in `Vora.Application.Media`) carries:

- `HasAllLibraryAccess` + `AllowedLibraryIds` (per-profile library scope)
- `HasAllRatings` + `AllowedRatings` + `BlockUnratedContent` (music content-rating filter, separate from movie/TV ratings)

Endpoints build it via `MusicEndpoints.BuildFilter(user)` using `AuthExtensions`. Every music repo query that returns tracks/albums/artists must apply this filter — it's the music equivalent of `ApplyAccessFilters` for `MediaItem`.

## Recommendation engine

Lives in `Vora.Application/Media/MusicRecommendationManager.cs` + `IMusicRecommendationRepository` (impl in `Vora.Infrastructure`).

**`GeneratedMix` entity** with `GeneratedMixKind`:

- `DailyMix` (slots 1–6) — per-profile, refreshed daily. Top-artist clusters by genre overlap with drift (~20% replacement per refresh).
- `DiscoverMix` — weekly. Similar artists not in your library (via Last.fm), filtered down to artists *in* your library, plus their top tracks.
- `MoodMix` — weekly. Hardcoded mood definitions (`Chill`, `Focus`, `Workout`, `Late Night`, etc.) matched against artist tags + album genres.
- `ReleaseRadar` — weekly. Top artists from last 60 days × tracks added to library in last 30 days.

Gates: profiles with `< 3 distinct artists` or `< 50 plays` are skipped (recommendation worker hits a `GetProfileIdsWithRecentActivityAsync` filter first).

**`RecommendationRefreshWorker`** background service ticks daily; runs `RefreshAllActiveProfilesAsync` (daily mixes) and `RefreshWeeklyMixesForAllAsync` (Discover, Mood, Release Radar) on the weekly cadence stored in `ServerSetting`.

Surfaced through `/api/music/recommendations/mixes`, rendered in the Music tab "Made for You" + "Moods" + "Release Radar" rows.

## Stations + radio

**`Station` entity** with `StationSeedKind` (`Artist` / `Track` / `Genre`).

- `IMusicRecommendationManager.StartRadioAsync(profileId, access, seed, size)` returns a `RadioQueueVM` with a seeded track list.
- `ExtendRadioAsync` is called by `PlayerContext` for endless radio — when the queue runs low it pulls more tracks excluding ones already played.
- Saved stations live under `/api/music/stations` and render on the Music tab home page.

`PlayerContext` tracks `currentRadioSeed`; when radio is active, near-end-of-queue triggers an `extendRadio` call.

## Artist similarity

`ArtistSimilarity` and `ArtistTag` entities cache Last.fm responses with a TTL. `MusicRecommendationManager.GetSimilarArtistsAsync` is cache-or-fetch: it returns cached entries if fresh, otherwise calls the Last.fm provider, then resolves remote names back to in-library artists via `GetArtistsByNamesAsync`. Out-of-library similar artists are dropped (intentional — Vora only surfaces what the user can actually play).

## Lyrics

`ILyricsProvider` is a Vora plugin. Implementations: `LrcLibLyricsProvider` (primary, free), `GeniusLyricsProvider` (fallback). `MusicManager.GetTrackLyricsAsync` tries embedded → external `.lrc` file → providers in registration order. Returns `LyricsResult` with `PlainLyrics`, `SyncedLyrics`, `IsSynced`. Surfaced in `NowPlayingFullscreen`.

## Last.fm

`ILastFmClient` plugin handles two flows:

- **Per-profile auth** (token + session) — used for **scrobbling**. `PlayerContext` records on threshold; the manager dispatches to Last.fm when the profile is connected.
- **No-session reads** — `GetSimilarArtistsAsync`, `GetArtistTopTagsAsync`. Used by recommendation manager regardless of whether any profile is connected.

## Server playback tracker

`IServerPlaybackTracker` (singleton DI). In-memory `ConcurrentDictionary<Guid, ServerPlaybackSessionVM>` keyed by profile id, 45-second TTL.

- `PlayerContext` sends a heartbeat every 10s while music is playing (`POST /api/music/playback/heartbeat`).
- Stop on close → `POST /api/music/playback/stop`.
- SignalR `ServerPlaybackUpdated` event fires after each heartbeat/stop.
- Music tab "Listening Now" panel listens for the event and re-fetches `/api/music/playback/active` (excluding current profile).

No persistence — restart wipes it.

## Admin music history

`/api/admin/music/history` and `/api/admin/music/summary` (server-admin only). Backend queries `TrackPlayHistory` joined with profiles, tracks, albums, artists. Frontend at `/admin/music-history` — summary cards + top-tracks/top-artists/plays-per-profile panels + filterable paginated table.

## Audio playback pipeline

**Streaming** — `GET /api/music/tracks/{id}/stream?quality=`:

- No `quality` param → original file, range-supported direct serve.
- `quality=low|medium|high` → FFmpeg subprocess pipes MP3 at 128/192/320 kbps. Cancellation via `HttpContext.RequestAborted`.

**`PlayerContext`** (`src/contexts/PlayerContext.tsx`) handles all playback. Music-specific features:

- **Queue + shuffle + repeat** — shuffle reorders the displayed queue; repeat modes `off/all/one`. `jumpToQueueIndex` for direct index navigation.
- **Endless radio** — when `currentRadioSeed` is set and the queue runs low, calls `extendRadio` and appends.
- **Gapless playback** — preloads next track when current is near end.
- **Crossfade** — Web Audio API. Per-device localStorage setting. Fade-in ramp on track change (0→1 over `min(crossfade, 3)`s).
- **EQ presets** — Web Audio `BiquadFilterNode`s applied in series after `MediaElementSource`.
- **Audio quality** — per-device localStorage (`audioQualityStore`). Frontend dispatches `audio-quality-changed` window event; `PlayerContext` listens, rebuilds the stream URL on the current track preserving position.

**`PlayableMedia.playbackContextType`** is the discriminator that controls UI:

- `'Music'` → music chrome (album art, Now Playing fullscreen, lyrics panel, queue panel).
- `'LiveRadio'` → live radio player.
- `'Podcast'` → podcast player (saves episode state, seeks to last position).

When `MainLayout` dispatches the player, it inspects this field to pick `GlobalVideoPlayer` vs `LiveRadioPlayer`.

## Audio hub page

`/audio` (`AudioHubPage`) has three tabs persisted to sessionStorage: **Music** (`MusicTab`), **Podcasts** (`PodcastsTab`), **Radio** (live radio stations from IPTV `IptvChannelKind.Radio` + Radio Browser feeds).

Music libraries don't appear in the left nav — they're reached via this hub.

## Plugins relevant to music

- `MusicBrainzArtworkProvider`, `FanartTvMusicArtworkProvider`, `TheAudioDbMusicArtworkProvider` — implement `IMusicArtworkProvider`. Surface in the metadata edit modal's image picker.
- `LrcLibLyricsProvider`, `GeniusLyricsProvider` — implement `ILyricsProvider`.
- `LastFmClientPlugin` — implements `ILastFmClient` and `IListeningDataProvider`.
- `ItunesPodcastDiscoveryProvider` — implements `IPodcastDiscoveryProvider`.

See `docs/plugins.md` for the plugin loader contract.

## Where to look first

| Want to change | Start here |
| --- | --- |
| New music endpoint | `Vora.Api/Endpoints/MusicEndpoints.cs` |
| Music query (player-facing) | `Vora.Infrastructure/Persistence/Repositories/MusicRepository.cs` |
| Recommendation algorithm | `Vora.Application/Media/MusicRecommendationManager.cs` |
| Recommendation query | `Vora.Infrastructure/Persistence/Repositories/MusicRecommendationRepository.cs` |
| Music tab UI | `Vora.Web/src/pages/Client/Audio/MusicTab.tsx` |
| Player behavior | `Vora.Web/src/contexts/PlayerContext.tsx` |
| New mix kind | `GeneratedMixKind` enum + add a generator method on the manager + call it from `RefreshWeeklyMixesForProfileAsync` |
