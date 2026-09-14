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

**Music has exactly one expanded view: `NowPlayingFullscreen`.** `LiveRadioPlayer` renders music only as the bottom bar — its older full-screen layout is for radio and podcasts. `usesNowPlayingScreen(media)` (`utils/nowPlayingScreen.ts`) is the single predicate for this, and three rules follow from it:

- **Starting** music (`playQueue`, `startRadio`) opens the screen. **Advancing** it (`nextTrack`, `previousTrack`, `jumpToQueueIndex`, auto-advance) goes through `playMedia` and leaves the screen as it is, so skipping from the mini bar doesn't throw the screen over whatever the viewer was browsing.
- `playMedia` sets `isMinimized` to `usesNowPlayingScreen(media)` directly, and closes the screen for anything that isn't music; `closePlayer` closes it too. **Don't reintroduce an effect that reconciles these after a media change.** One did, deferred through `queueMicrotask` — it forced `isMinimized = true` on every track change, so a fresh play landed collapsed, and because the microtask captured `currentMedia` from the render that scheduled it, it could undo state set after it.
- Every mini-bar expand control (artwork, title, the chevron) opens `NowPlayingFullscreen` for music. Routing any of them to `setMinimized(false)` shows the radio layout, which has no lyrics, queue panel or audio settings.

**Music and live radio share one full-screen frame.** `components/Player/NowPlaying/` holds `NowPlayingShell` (blurred artwork backdrop, glass header, main area, control bar; owns D-pad navigation, focus-on-open and Escape-to-minimize), `NowPlayingArtwork`, and the controls (`NowPlayingControlRow`, `NowPlayingPill`, `NowPlayingIconButton`, `NowPlayingPlayButton`, `NowPlayingVolume`). `NowPlayingFullscreen` and `RadioNowPlaying` are both built from them — change the look there, not in either screen, or the two drift apart again.

`RadioNowPlaying` is what `LiveRadioPlayer` renders when a `LiveRadio` session is expanded. It has previous/next **station** around play, an On air / Tuning in / error status, a Stations panel in place of the queue, and volume. **It has no seek bar and no skip back/forward**, and neither does the radio mini bar: a live stream has no position to move within. Those skips used to be gated on `isAudioOnDemand || canTimeshift`, which showed them on every station for any profile with the IPTV timeshift permission. They are now `isAudioOnDemand` only — podcasts keep them, and podcasts still use `LiveRadioPlayer`'s older expanded layout.

`PlayerContext.nowPlaying.test.tsx` renders the real provider and pins these; it fails against the previous effect-based version.

The now-playing control bar holds transport in the centre and the panel toggles — like, lyrics, queue, audio settings — on the right, stacking to two rows below `md`. The audio settings panel is anchored to the control bar (`absolute bottom-full`), not to the viewport, so it opens above the bar at any height. The panel toggles are labelled pills (icon plus "Lyrics" / "Queue" / "Audio"), not bare icons — a native `title` tooltip only appears after a hover delay and never on a remote, so an unlabelled icon was the only cue.

**Lyrics stay on across tracks.** Turning them on sets a preference (`lyricsWanted`), not a per-track flag; the panel is open when that preference is set and the current track has lyrics or is still loading them. A track with no lyrics closes the panel without clearing the preference, and it reopens on the next track that has some. The toggle renders when the track has lyrics or the panel is open on its loading state.

**The active synced line sits a third of the way down the panel** (`lyricsScrollTop` in `utils/lyricsScroll.ts`), clamped at the top of the list. Before the first line starts, the lyrics therefore begin at the top rather than below empty space. The list used to be padded 35vh top and bottom with the active line centred, and auto-scroll only began once a line was active, so a song with a long intro showed a third of a screen of nothing until the singing started. Scrolling is instant when the panel opens or the track changes, and smooth while following along.

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
