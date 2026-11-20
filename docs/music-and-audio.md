# Music & audio

How the music subsystem is organized — domain model, recommendation engine, playback, and the audio-only surfaces (Music tab, stations, podcasts).

## Domain model

Music sits inside the regular `MediaItem` TPH hierarchy:

- **`Artist`** — `LockableEntity`. Belongs to a `MediaLibrary`. Has `Name`, `SortName`, `Biography`, plus artwork (`ArtworkUrl`, `BackgroundUrl`, `BannerUrl`, `ClearLogoUrl`).
- **`Album`** — `LockableEntity`. FK to `Artist` and `MediaLibrary`. Has `Title`, `Year`, `Genre` (single string, not a join), `AlbumArtist`, `IsCompilation`, plus artwork (`ArtworkUrl`, `BackgroundUrl`, `DiscArtUrl`). An album with no cover **displays** the artist's image: track-level `AlbumArtworkUrl` fields are coalesced server-side (`AlbumCoverArt`), while `AlbumVM.ArtworkUrl` stays the album's own value (the edit modal posts it back) and the fallback travels separately as `AlbumVM.ArtistArtworkUrl`, resolved on the web by `utils/albumCover.ts`.
- **`Track : MediaItem`** — FK to `Album`. Has `TrackNumber`, `DiscNumber`, `DurationSeconds`, `Artist` string (per-track, used for compilations), audio metadata (`AudioCodec`, `SampleRate`, `Bitrate`), `HasEmbeddedLyrics`, `ExternalLyricsPath`.
- **`TrackLike`** — per-profile heart.
- **`TrackPlayHistory`** — every play that crosses the Spotify "long enough to count" threshold (≥30s or ≥50% of track). Drives Recently Played, Top Tracks, On Repeat heuristics, recommendation seeding, scrobbles.

Music libraries are filtered out of the left nav (they're surfaced via the Music tab instead). How a scanned file finds its album and track is in [`music-library-scanning.md`](music-library-scanning.md).

## Access filtering

`MusicAccessFilter` (in `Vora.Application.Media`) carries:

- `HasAllLibraryAccess` + `AllowedLibraryIds` (per-profile library scope)
- `AllowedRatings` + `BlockUnratedContent` (music content-rating filter, independent of movie/TV ratings; `HasAllRatings` is derived — empty allowlist = open)

Endpoints build it via `AuthExtensions.GetMusicAccessFilter()`. Every music query applies it through `MusicAccessQuery.ApplyMusicAccess` / `ApplyMusicRatings` — the only place the rule is written (`MusicAccessRuleIsWrittenOnceTests` enforces it).

## The profile's likes and ratings on a page

The album and artist pages load the profile's marks with what they show, not after it (`PostgresMusicPageTests`):
- **Songs:** each song comes with the profile's like and rating (`ViewerTrack`, `MusicRepository.ForViewer`).
- **Albums and the artist:** each comes with the profile's rating (`ViewerAlbum`, `ArtistPage`, `AlbumPage`).

| Request | Queries |
|---|---|
| `GET /api/music/albums/{id}` | 2: the album, its artist, the profile's rating of it and the artist's backdrop; then its songs |
| `GET /api/music/artists/{id}` | 2: the artist, the profile's rating and its tags; then its albums |
| `GET /api/music/artists/{id}/tracks` | 1 |
| `GET /api/music/artists/{id}/top-tracks` | 1 |

Before, each page loaded the songs, then the profile's likes of them, then its ratings of them, and the artist's songs looked up the artist's album ids first: 6, 5, 4 and 4 queries.

A mood's song list and shuffle choose their songs in a query of their own, then take the profile's marks of those songs in one more (`GetTrackMarksAsync`).

The album page borrows the artist's backdrop when the album has none. The backdrop is read through the access filter in the same query, so a profile that may not see the artist gets none.

## Where a track's Clean / Explicit comes from

1. **The file's tags** (scanner `DetectAdvisory`): MP4 `rtng`; ID3 `TXXX` and Vorbis `ITUNESADVISORY` / `PARENTAL_ADVISORY` / `EXPLICIT`; `[Explicit]` in the title. A tag **always wins** — on rescan it replaces a provider's answer (`Track.ContentRatingProvider` goes back to null). A `LockedFields["ContentRating"]` lock beats both.
2. **An `IMusicContentRatingProvider`** fills only what the tags left blank. Built in: `DeezerContentRatingProvider` (no key). `MusicContentRatingRefresher` runs nightly and after every music scan:
   - A track with an **ISRC** (`Track.Isrc`, read from the file) is looked up exactly — explicit and clean edits have different ISRCs.
   - Otherwise, every edition of the album is fetched and `MusicEditionMatcher` takes the **strictest** answer: Clean only if every matching edition says Clean. Deezer carries both editions of e.g. *The Eminem Show* with near-identical durations, so an untagged file can't be told apart — erring to Explicit is the safe direction.
   - Every asked track is stamped `ContentRatingCheckedAt`; an unanswered one is asked again after 90 days. A provider that stops answering ends the run.
3. **By hand** (admin): the track edit modal offers Explicit / Clean / None only (`MusicContentRating.TryNormalize`; anything else is a 400, since the music allowlist knows no other value). A changed rating is **locked**, including a change to None, so neither a rescan nor the provider undoes it. The lock doesn't block the admin's own next edit. Unlocking without changing the value hands it back to tags and the provider. `PUT /api/music/albums/{id}/content-rating` sets and locks every track of an album.
   - `TrackVM.contentRatingSource` is `None | FileTag | Provider | Manual`, and the edit modal says which. Clients show `ContentRatingBadge` ("E" / "C") in every track list and an "E" on the album header when any track is explicit.

## Recommendation engine

Lives in `Vora.Application/Media/MusicRecommendationManager.cs` + `IMusicRecommendationRepository` (impl in `Vora.Infrastructure`).

**`GeneratedMix` entity** with `GeneratedMixKind`:

- `DailyMix` (slots 1–6) — per-profile, refreshed daily. Top-artist clusters by genre overlap with drift (~20% replacement per refresh).
- `DiscoverMix` — weekly. Similar artists not in your library (via Last.fm), filtered down to artists *in* your library, plus their top tracks.
- `MoodMix` — weekly. Hardcoded mood definitions (`Focus`, `Energetic`, `Chill`, `Late Night`) matched against artist tags + album genres. Separate from **Browse by Mood**, which reads each song's own moods.
- `ReleaseRadar` — weekly. Top artists from last 60 days × tracks added to library in last 30 days.

Gates: profiles with `< 3 distinct artists` or `< 50 plays` are skipped (recommendation worker hits a `GetProfileIdsWithRecentActivityAsync` filter first).

**`RecommendationRefreshWorker`** background service ticks daily; runs `RefreshAllActiveProfilesAsync` (daily mixes) and `RefreshWeeklyMixesForAllAsync` (Discover, Mood, Release Radar) on the weekly cadence stored in `ServerSetting`.

Surfaced through `/api/music/recommendations/mixes`, rendered in the Music tab "Made for You" + "Moods" + "Release Radar" rows. Every generator passes its songs through `DedupeBySong`, so a song the library holds twice (a single and its album, a remaster, a FLAC beside an MP3) is in a mix once, as its best copy — see **Same song** in `docs/ai-playlists.md`. A mix's tracks carry `AlbumArtworkUrl` (`AlbumCoverArt.For`), and a queued mix song uses it, so the queue and Now Playing show each song's own cover rather than the mix's.

## Stations + radio

**`Station` entity** with `StationSeedKind` (`Artist` / `Track` / `Genre`).

- `IMusicRecommendationManager.StartRadioAsync(profileId, access, seed, size)` returns a `RadioQueueVM` with a seeded track list.
- `ExtendRadioAsync` is called by `PlayerContext` for endless radio — when the queue runs low it pulls more tracks excluding ones already played.
- Saved stations live under `/api/music/stations` and render on the Music tab home page.

`PlayerContext` tracks `currentRadioSeed`; when radio is active, near-end-of-queue triggers an `extendRadio` call.

## Artist similarity

`ArtistTag` rows are the artist's Last.fm top tags, saved by `MusicPopularityRefresher` from the `artist.getInfo` answer it already fetches (no extra calls; refreshed with popularity every 30 days, most used first; on a music library's Manage page **Refresh popularity** — the same `POST /api/libraries/{id}/ratings` that refreshes ratings elsewhere — refreshes every artist in that library now, `RefreshLibraryArtistsAsync`, instead of walking its songs for movie/TV ratings that don't apply). They feed Mood Mixes, AI playlist song descriptions and the artist page (`ArtistDetailVM.Tags`); nothing else fetches them. The same answer fills `Artist.Biography` (Last.fm's text, without its "Read more" link, licence line or markup) when the artist has none and the field isn't locked; the artist page shows it under **About** with a "From Last.fm" credit (CC BY-SA). **Quality and feel**: `AudioQuality.For` words a song's codec/sample rate/bitrate once for every client (`Hi-Res · FLAC 96 kHz` above 48 kHz lossless, `Lossless · FLAC 44.1 kHz`, `MP3 · 320 kbps`); `AlbumDetailVM.Quality` is the album's most common format at its best rate and `AlbumDetailVM.Moods` its tracks' four most common moods; `GET /api/music/tracks/{id}/info` (`TrackInfoVM`, access-filtered) gives one song's quality, moods, energy, themes, good-for and instrumental flag for Now Playing — fetched per song like lyrics, so track lists carry none of it. `ArtistSimilarity` caches Last.fm responses with a TTL. `MusicRecommendationManager.GetSimilarArtistsAsync` is cache-or-fetch: it returns cached entries if fresh, otherwise calls the Last.fm provider, then resolves remote names back to in-library artists via `GetArtistsByNamesAsync`. Out-of-library similar artists are dropped (intentional — Vora only surfaces what the user can actually play).

## Lyrics

`ILyricsProvider` is a Vora plugin. Implementations: `LrcLibLyricsProvider` (primary, free), `GeniusLyricsProvider` (fallback). `MusicManager.GetTrackLyricsAsync` tries embedded → external `.lrc` file → providers in registration order. Returns `LyricsResult` with `PlainLyrics`, `SyncedLyrics`, `IsSynced`. Surfaced in `NowPlayingFullscreen`. The For You song rows (Because you played, Recently Played) show each song's Last.fm listeners under it, as Recently Added shows album plays; `MusicManager.MapArtistTracksWithLikesAsync` and `MusicRecommendationManager.MapArtistTrack` carry `GlobalListeners` / `GlobalPlays`.

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

## Recording a play

`POST /api/music/tracks/{trackId}/played` with `{ durationListenedSeconds, completed, source?, sourceId? }`. **Every client posts here** — web, TV, phone — so the rules below are the contract, not the web player's implementation.

Returns `200 { recorded, outcome }`, where `outcome` is `Recorded`, `BelowThreshold` or `UnknownTrack`. A post that does not qualify is discarded on purpose, which is a correct result rather than a client error, so it is not a 4xx — but it is reported, because otherwise a client posting at the wrong moment sees success and no history and has nothing on either side to connect the two. `UnknownTrack` is kept distinct so a bad track id is not mistaken for a threshold bug.

- **Post once, when the listen ENDS**: the track finished, the queue moved on, or the app is closing. Not when a threshold is crossed. The web player held a `recordedPlayTrackIdRef` set at the threshold and checked the same ref in `onEnded`, so `onEnded` never fired and every row stored "~30 seconds, not completed" regardless of what was heard.
- **`durationListenedSeconds` is the FURTHEST position reached**, not the latest. Seeking back to replay a passage must not reduce it.
- **`completed`** means the track played out. It is trusted over the position, because players stop reporting position slightly before the end.
- **`source` and `sourceId`** say what the song was played from: `Album`, `Playlist`, `SmartPlaylist` or `Mix` (every generated mix, AI playlists, Blends and requests included) with that one's id. Each queue item carries its own, so a song added to the queue from an album still says the album while a playlist plays. Anything else (an artist page, search, Liked Songs, radio, a For You song tile) sends neither. A source without an id is stored as none (`TrackPlayHistory.Source` / `SourceId`, `MusicPlaySource`); plays recorded before the columns existed have none.

### Recently Played

`GET /api/music/history/recently-played?limit=` (default 20, at most 50; `RecentlyPlayedManager`) is the For You row, newest first. It walks the profile's last 500 plays: a play from a playlist, smart playlist or mix stands for that one, so a whole evening on one playlist is a single tile at its latest play; any other play, an album's included, stands for its song. Each item is `{ kind, id, title, subtitle, artworkUrl, artworkUrls, mixKind, trackCount, playedAt, track }`:

- `kind` `Mix`, `Playlist` or `SmartPlaylist`: `id` is what to open, `subtitle` the row it comes from ("Made for you by AI", "Your request", "Blend", "Bridge", "Made for You", "Release Radar", "Mood", "Playlist", "Smart playlist"). A mix has the same mosaic as the AI playlist row (`MixCoverArt`); a playlist its own cover, else its poster mosaic.
- `kind` `Track`: `track` is the `ArtistTrackVM` the other song rows use and `subtitle` its album, so the tile shows the song and its album. Tapping one plays the row's songs from there.

A tile whose source has gone is left out and older history fills the row: a weekly AI playlist replaced by the next week's, a playlist deleted or no longer shared, a song the profile may no longer play. The clients put the row first on For You, right after the shortcut tiles and above Made for you by AI, and hide it when it is empty. `GET /history/recent` (songs only) is unchanged for older clients.

Each play `RecordTrackPlayAsync` stores sends **`MusicHistoryUpdated`** (SignalR, the profile's group, payload the profile id). The clients reload only this row when it arrives, so a playlist being played shows up on For You without leaving the page, on the device playing it and any other on the profile. A play is posted when the song ends or is skipped, so the row moves with the first song saved, not when playback starts. A failed send is logged and the play is still `Recorded`. The web's `useRecentlyPlayed` hook holds the row and its reload.

### Continue listening

A profile can pick up where a music queue stopped, on any device. `MusicResumePoint` (table `MusicResumePoints`, `MusicResumeManager`) keeps **one point per profile and per source** the queue was started from (`Album`, `Playlist`, `SmartPlaylist`, `Mix`, or `None` for an ad-hoc queue: Play all on an artist, search, Liked Songs, a single song, which share one point), the newest 20 per profile (`MusicResumeLimits.MaxPoints`; older ones go on the next save). A point is the queue's song ids in play order, as shuffled if it was shuffled (at most 300: a longer queue is cut to a window that starts 50 songs before the current one, `MusicResumeManager.Window`), and the index of the current song. There is no position inside the song: continuing starts that song from its beginning, which was decided against the cost of writing a position every few seconds for songs that last three minutes.

- `PUT /api/music/resume` `{ source, sourceId, trackIds, index }` saves a point and answers `204`; **an empty `trackIds` clears the source's point** (the clients send it when a queue finishes by itself). `400` for a source other than `None` with no id, an index outside the queue, an unknown source or more than 1000 ids (`MusicResumeLimits.MaxAccepted`). Each save or clear sends **`MusicResumeUpdated`** (SignalR, the profile's group, payload the profile id); a failed send is logged and the save stands.
- `GET /api/music/resume?limit=` (default 10, at most 20) is the row, newest first: a `RecentlyPlayedItemVM` for the point (`RecentlyPlayedManager.TilesAsync`, the same tiles Recently Played draws) plus `source`, `sourceId` and `trackIndex`. A queue from a playlist, smart playlist or mix is that tile with the current song's title as its subtitle; one from an album or with no source is a `Track` tile (the current song, its album as the subtitle) whose `source` still names the album. A tile whose playlist or mix has gone or is no longer visible, or whose current song is no longer playable, is left out (a deleted source's point stays until it ages out of the newest 20).
- `GET /api/music/resume/queue?source=&sourceId=` is what to play: the queue's songs in saved order minus any that no longer exist or that the profile may not play (the profile's music access applies), and `index`, the saved current song, the next one that survived if it is gone, else 0. `404` when there is no point or nothing playable is left.
- **Saving is the client's job** and happens when the song changes, when playback pauses and when the player is closed, never on a timer: a burst of skips is one save about a second after the last. The web's `useMusicResumeSaver` (in `PlayerContext`) sends the snapshot (`utils/musicResume.ts`) and skips a state it already sent; closing the player keeps the point, a queue that ends by itself clears it, and radio and anything that is not music is never saved. A server that answers 404 or 405 has no endpoint, so the web stops saving to it until the page reloads.
- **Showing it:** Music For You has a **Continue Listening** row above Recently Played (`ContinueListeningRow`, `useMusicResume`), and Home has a **Continue Listening** smart list (`docs/smart-lists.md`). Picking a tile calls `useResumeMusic`, which fetches the queue, queues its songs tagged with the source so later plays record it, and starts at `index`. Both hide when empty and reload on `MusicResumeUpdated`.
- **Recently Played on the web** still opens the playlist or mix; the Android, Roku and Apple TV apps play it.

### What counts as a play

`PlayQualification` in `Vora.Domain` is authoritative and `MusicManager.RecordTrackPlayAsync` enforces it — a post below the bar is **discarded, not stored** (logged at Debug, and reported as `BelowThreshold`). A client that posts at its own threshold loses those plays; check `recorded` while building one.

```
qualifies = listened >= 240s  OR  listened / trackDuration >= 0.5
```

The 240s arm is a **ceiling, not a floor**: for anything under 8 minutes the 50% arm always fires first, so a 3-minute song qualifies at 1:30 and only tracks longer than 8 minutes benefit from the absolute cap. A track with no scanned duration falls back to 30s, since there is no fraction to take. This is Last.fm's own scrobble rule, and a scrobble goes out on the same call.

`Vora.Web/src/utils/playQualification.ts` mirrors it so the player does not post something that will be dropped. `PlayQualificationParityTests` reads that file and fails if the constants or the comparisons drift. **A native client should mirror it the same way** — the copy is an optimisation, never the decision.

### How plays are scored

Recommendations weight each row by how much was heard rather than counting it as one (`PlayQualification.Weight`): played out or >=95% → 1.0, >=75% → 0.8, >=50% → 0.5, qualifying but shallower → 0.25. Unknown depth or unknown track length → 0.5, because that is absence of evidence rather than a shallow listen. Used by `GetTopArtistsForProfileAsync` and `GetTopTracksByArtistAsync`; `GetCoPlayedArtistsAsync` stays on distinct-listeners-then-plays, since the gate already keeps shallow listens out of the table entirely.

## Admin music history

`/api/admin/music/history` and `/api/admin/music/summary` (server-admin only). Backend queries `TrackPlayHistory` joined with profiles, tracks, albums, artists. Frontend at `/admin/music-history` — summary cards + top-tracks/top-artists/plays-per-profile panels + filterable paginated table.

## Audio playback pipeline

**Play** — `POST /api/music/tracks/{id}/play` returns `{ url, container, contentType }`.
The URL carries a signed token and no file extension, so the container (`mp3`,
`flac`, `m4a`, …) and its MIME type are stated up front: a client whose audio
player needs the format before it opens the stream (Roku's `Audio` node) would
otherwise have to fetch the first bytes of every track to sniff it. The values
describe what the stream will actually be — a transcode request reports `mp3`,
a direct play reports the file's own container.

**Streaming** — `GET /api/music/tracks/{id}/stream?quality=`:

- No `quality` param → original file, range-supported direct serve.
- `quality=low|medium|high` → MP3 at 128/192/320 kbps from `IAudioTranscodeService.GetTranscodedFileAsync`, served like the original (`Results.File`, range-supported). FFmpeg encodes the **whole track to a file** first: a piped stream has no length and no ranges, so the browser reported its duration as `Infinity` (the seek bar's `<input type=range>` fell back to `max=100` and sat full, the end time read 0:00) and crossfade, gapless preload and the play-count threshold, which all need the duration, never fired. A 4-minute FLAC takes about a second. The file lands in `{TranscoderTempDirectory}/audiocache/{trackId:N}_{kbps}k_{fingerprint}.mp3` (fingerprint of the source's path, size and mtime, so a changed source is a miss); concurrent requests for the same file share one encode; it's written as `*.tmp` and moved into place. The folder is capped at 1 GB: after each new encode `PruneCache` drops the least recently played files (a hit refreshes a file's mtime at most once an hour) and partial files older than an hour. If FFmpeg fails the original file is served.
- `POST /api/music/tracks/{id}/play` returns `Quality` (`AudioQualityVM`) for a transcode — what the stream carries, e.g. `MP3 · 192 kbps` — and null for direct play. The client keeps it as `PlayableMedia.streamQuality`, and the Now Playing chip shows it in place of the file's own quality, naming the source in its tooltip.

**`PlayerContext`** (`src/contexts/PlayerContext.tsx`) handles all playback. Music-specific features:

- **Queue + shuffle + repeat** — shuffle reorders the displayed queue; repeat modes `off/all/one`. `jumpToQueueIndex` for direct index navigation.
- **Endless radio** — when `currentRadioSeed` is set and the queue runs low, calls `extendRadio` and appends.
- **Gapless playback** — preloads next track when current is near end.
- **Crossfade** — Web Audio API. Per-device localStorage setting. Fade-in ramp on track change (0→1 over `min(crossfade, 3)`s).
- **EQ presets** — Web Audio `BiquadFilterNode`s applied in series after `MediaElementSource`. The graph ends `gain → AnalyserNode (fftSize 2048) → destination`. The context, gain and analyser are made once; the source follows the player's `<video>` element (`ensureAudioGraph` connects a new element when the player has replaced it, since `GlobalVideoPlayer` keys it by session; before, closing the player or playing a show left the graph listening to the old element and the Synth flat); `usePlayer().getAudioAnalyser()` returns that analyser (null until a song has played) for the **Synth** visualizer (`NowPlaying/SynthVisualizer.tsx`: 40 log-spaced bands mirrored about the centre, bass in the middle; maths in `utils/synthBands.ts`; drawing in `NowPlaying/synthRenderers.ts`; 10 fps without glow under `prefers-reduced-motion`; a gentle idle motion when there is no analyser). It has three styles, picked from the arrow on the right half of the Synth split pill (`NowPlayingSplitPill`, `.vora-split-pill`; menu `SynthStyleMenu`): **Bars** (rounded bars growing up and down from a centre line, the lower half fading, floating peak dots, a halo that swells on bass onsets), **Waves** (three layered, smoothed bands of light) and **Ring** (spokes around the cover, which pulses on the beat). Colours come **from the album art** by default: `useCoverArt` loads the cover through the same-origin `/api/artwork/thumb` (500 px), keeps it for Ring, and `paletteFromPixels` (`utils/artPalette.ts`) takes a main hue, a second hue and a light tint from a 32×32 sample, brightened to glow; a grey cover, or one the page can't read, falls back to the theme accent, which **Theme accent** always uses. The Lyrics and Synth toggles and the Synth style and colours are remembered per device (`nowPlayingViewStore`: `now_playing_lyrics`, `now_playing_synth`, `now_playing_synth_style`, `now_playing_synth_colors`). With either panel open the song's details sit in one row (`NowPlayingSongBar`): cover left, title and artist centred, quality and mood right; Ring drops that cover since it is in the ring. With both on, the synth takes `flex-[3]` and the faded lyrics panel `flex-[2]` under it; with both closed the big cover returns.
- **Audio quality** — per-device localStorage (`audioQualityStore`). Frontend dispatches `audio-quality-changed` window event; `PlayerContext` listens, resolves the current track again at the new quality and resumes it at the same position through `startPosition` (applied on `loadedmetadata`, since the first play at a quality waits for the encode), and drops the preloaded next track so it is fetched at the new quality too.

**`PlayableMedia.playbackContextType`** is the discriminator that controls UI:

- `'Music'` → music chrome (album art, Now Playing fullscreen, lyrics panel, queue panel).
- `'LiveRadio'` → live radio player.
- `'Podcast'` → podcast player (saves episode state, seeks to last position).

When `MainLayout` dispatches the player, it inspects this field to pick `GlobalVideoPlayer` vs `LiveRadioPlayer`.

**Music has exactly one expanded view: `NowPlayingFullscreen`.** `LiveRadioPlayer` renders music only as the bottom bar — its older full-screen layout is for radio and podcasts. `usesNowPlayingScreen(media)` (`utils/nowPlayingScreen.ts`) is the single predicate for this, and three rules follow from it:

- **Starting** music (`playQueue`, `startRadio`) opens the screen. **Advancing** it (`nextTrack`, `previousTrack`, `jumpToQueueIndex`, auto-advance) goes through `playMedia` and leaves the screen as it is, so skipping from the mini bar doesn't throw the screen over whatever the viewer was browsing.
- `playMedia` sets `isMinimized` to `usesNowPlayingScreen(media)` directly, and closes the screen for anything that isn't music; `closePlayer` closes it too. **Don't reintroduce an effect that reconciles these after a media change.** One did, deferred through `queueMicrotask` — it forced `isMinimized = true` on every track change, so a fresh play landed collapsed, and because the microtask captured `currentMedia` from the render that scheduled it, it could undo state set after it.
- Every mini-bar expand control (artwork, title, the chevron) opens `NowPlayingFullscreen` for music. Routing any of them to `setMinimized(false)` shows the radio layout, which has no lyrics, queue panel or audio settings.

**Music, live radio and podcasts share one full-screen frame.** `components/Player/NowPlaying/` holds `NowPlayingShell` (blurred artwork backdrop, glass header, main area, control bar; owns D-pad navigation, focus-on-open, Escape-to-minimize, and the player keys through `playerKeys`: Space, Shift+Left/Right to seek, Shift+Up/Down for the volume, see `docs/frontend-client-ui.md`), `NowPlayingArtwork`, and the controls (`NowPlayingControlRow`, `NowPlayingPill`, `NowPlayingIconButton`, `NowPlayingPlayButton`, `NowPlayingSeekBar`, `NowPlayingSkipButton`, `NowPlayingVolume`). `NowPlayingFullscreen`, `RadioNowPlaying` and `PodcastNowPlaying` are all built from them — change the look there, not in one screen, or they drift apart again. Times go through `formatPlaybackTime` (`utils/playbackTime.ts`), which shows hours; episodes routinely pass one.

`RadioNowPlaying` is what `LiveRadioPlayer` renders when a `LiveRadio` session is expanded. It has previous/next **station** around play, an On air / Tuning in / error status, a Stations panel in place of the queue, and volume. **It has no seek bar and no skip back/forward**, and neither does the radio mini bar: a live stream has no position to move within. Those skips used to be gated on `isAudioOnDemand || canTimeshift`, which showed them on every station for any profile with the IPTV timeshift permission. They are now `isAudioOnDemand` only — podcasts keep them.

`PodcastNowPlaying` is what `LiveRadioPlayer` renders for an expanded `Podcast` session: a seek bar, skip back 10 / play / skip forward 30, and volume. No previous/next — a podcast has no queue to step through. With music, radio and podcasts all on the shared screens, `LiveRadioPlayer`'s own expanded layout is gone; it renders only the mini bar and dispatches to the right screen.

`PlayerContext.nowPlaying.test.tsx` renders the real provider and pins these; it fails against the previous effect-based version.

The now-playing control bar holds transport in the centre and the panel toggles — like, lyrics, queue, audio settings — on the right, stacking to two rows below `md`. The audio settings panel is anchored to the control bar (`absolute bottom-full`), not to the viewport, so it opens above the bar at any height. The panel toggles are labelled pills (icon plus "Lyrics" / "Queue" / "Audio"), not bare icons — a native `title` tooltip only appears after a hover delay and never on a remote, so an unlabelled icon was the only cue.

**Lyrics stay on across tracks.** Turning them on sets a preference (`lyricsWanted`), not a per-track flag; the panel is open when that preference is set and the current track has lyrics or is still loading them. A track with no lyrics closes the panel without clearing the preference, and it reopens on the next track that has some. The toggle renders when the track has lyrics or the panel is open on its loading state.

**The active synced line sits a third of the way down the panel** (`lyricsScrollTop` in `utils/lyricsScroll.ts`), clamped at the top of the list. Before the first line starts, the lyrics therefore begin at the top rather than below empty space. The list used to be padded 35vh top and bottom with the active line centred, and auto-scroll only began once a line was active, so a song with a long intro showed a third of a screen of nothing until the singing started. Scrolling is instant when the panel opens or the track changes, and smooth while following along.

## Music page

`/music` (`MusicPage` → `MusicTab`) carries its own header: the "Music" title with a round search toggle (`MusicSearchToggle`) beside it. The search field only renders while open, so it costs no vertical space; closing it clears the query. Under the header, four sub-tabs — **For You** (Recently Played, the AI playlists, mixes, stations, Because You Played, Recently Added, Top Artists and the Liked Songs / Top Tracks / Genres / Year in Music shortcuts), **Artists** (`MusicArtistsGrid`), **Albums** (`MusicAlbumsView`) and **Playlists** (the existing `PlaylistsPage` with `lockedType="music"` and `showMixes={false}` — no separate playlist system). The selected sub-tab persists in `localStorage` (`music_sub_tab`). Drill-ins (artist, album, mix, genre…) replace the tab content with a breadcrumb rooted at the active sub-tab; the tab content stays mounted but hidden, so returning to Albums keeps the pages already loaded. Tiles are `MediaCard size="xs"`, the dense card size. Albums are always listed by `AlbumBrowser` (`MusicAlbumsView.tsx`): the paged `GET /api/music/albums` list with the A–Z / Recently added / Newest / Popular sorts. The Albums tab uses it for the whole library, and a genre's page passes `genre`, so it pages through every album in the genre rather than the 60-album sample `GET /music/genres/{genre}` returns. That response carries the real `albumCount` and every artist in the genre, drawn as `MediaCard shape="circle" size="xs"` in a `MediaRow`. Album lists and tiles show the album's Last.fm `globalPlays` with `LastFmCount` (the mark + compact figure; nothing when null), as tracks show `globalListeners`. A genre is drawn by `BrowseTile` on the Genres grid and in its page header, with the same cover: the lowest-id playable album in the genre that has artwork (`sampleArtworkUrl` on both the summary and the genre response). **Browse by Mood**: `GET /api/music/moods` lists the fixed moods (`SongMoods`, see `docs/ai-playlists.md`) that at least 5 of the viewer's songs have, in list order, each with its song count and the cover of its most-listened song that no earlier mood used; `GET /moods/{mood}/tracks?skip&take` pages its songs most-listened first (`take` ≤ 200, `totalCount` for the whole mood) and `GET /moods/{mood}/shuffle?count` draws up to 200 at random from all of them. All three run the music access filter; an unknown mood is 404 (variants like "mellow" resolve). On For You the row (`BrowseByMoodRow`) is hidden while no song has been described; `MusicMoodView` lists 100 at a time with **Show more**.

**Music playlists open in the tab, like a mix.** A playlist, a smart playlist or an AI-made playlist picked on Playlists or For You, a new one just made (the create flow on `PlaylistsPage`, **Save to my playlists** on a mix) and a saved copy all take the nav view `'playlist'` / `'smartPlaylist'` (`musicNavState.ts`, with `playlistId` and `savedCopy`) instead of navigating to `/playlist/:id`. The breadcrumb reads *Playlists / Name* (*For You / Name* when it was picked there); Back and the breadcrumb return to the list it came from; the view survives a reload through `music_nav_state`; Delete, or a playlist that has gone, returns to the list. `PlaylistsPage` takes `onOpenPlaylist` (what opens a playlist) and `refreshKey`: the list stays mounted behind an open playlist, so the tab bumps the key when a playlist view closes, and a playlist made, renamed, shared or deleted in front of it shows on the way back (the AI playlists reload too). `PlaylistDetailsPage` and `SmartPlaylistDetailsPage` take a `host` (`EmbeddedPlaylistHost`: the id, whether it is a saved copy for the banner, `onLoaded(name)` for the breadcrumb, `onDeleted`, `onOpenCopy`, `onMissing`) and drop their own page padding; edit, delete, sharing, reordering and adding songs work as on the page. The `/playlist/:id` and `/smart-playlist/:id` routes are unchanged, for deep links, shared links, video playlists and the standalone Playlists page, and the Edit and Delete buttons on them show the pointer cursor like every other control.

**All albums** — `GET /api/music/albums?offset=&limit=&sort=&libraryId=` (`ListAlbums`) returns `AlbumPageVM { items, total, offset, limit }` for the profile's accessible libraries and ratings. `sort` is `RecentlyAdded` (default) or `Alphabetical` (`SortTitle ?? Title`), exact case; `limit` defaults to 60 and is capped at 200. An album with no track the profile may play is left out, as with Recently Added.

Podcasts (`/podcasts`) and Radio (`/radio`) are separate pages. **The Radio page draws each station as the app's square tile**, as Home's station rows and the album grids do, never a card box or a circle: `MediaCard shape="square" size="xs" fill` in a `MediaGrid size="xs"` per group, the logo whole and inset (`imageFit="contain"`, `uncachedImage`), the radio glyph in the placeholder when there is no logo or it fails (`placeholderIcon`), the name then the country code under it, a favourite's star on the art's top-left (`topLeftBadge`), a hidden station's whole tile at 45% while hidden stations are shown, and the favourite / hide menu on right-click or a long press (`onMenu`). Music libraries don't appear in the left nav — they're reached via the Music page. **The Podcasts page covers every connected server**, so its sidebar entry names none. Browse Catalog always did; Subscriptions and New Episodes do too (`podcastService.getSubscriptionsOnEveryServer` / `getRecentEpisodesOnEveryServer`). A podcast subscribed on several servers is listed once, matched by feed address (`PodcastSubscriptionVM.FeedUrl`; a server too old to send it keeps its own entries), and the copy shown is the page's server's, else the first server's. Its episodes, plays and progress all go to the server that copy came from (`serverId` on each row, and on `PlayableMedia`, which `LiveRadioPlayer` saves progress to), and Unsubscribe removes it from every server it is on. A server with podcasts turned off (403) is skipped rather than reported as unreachable. Search and a pasted feed still subscribe on the page's server.

**An episode plays from its feed's address, and falls back to where that address's redirects end.** Enclosures are usually a chain of tracking redirects (The Boyscast's: podtrac → pscrb.fm → clrtpod.com → mgln.ai → arttrk.com → prfx.byspotify.com → traffic.libsyn.com → the file on `delivery-edge.libsyn.com`). The browser walks it itself, except behind a tracker blocker: uBlock Origin's privacy list rewrites several of those hops and the media request dies at arttrk.com (`net::ERR_INVALID_ARGUMENT`, a "Format error" on the element), and the episode used to sit there never starting. On a podcast's media `error`, `LiveRadioPlayer` asks `GET /api/podcasts/episodes/{episodeId}/audio` (`podcastService.resolveEpisodeAudio`, on the episode's own server) and plays the `url` it answers, resuming where the element was. A second error, a failed lookup, or an answer that is the address that already failed shows "This episode could not be played…" under the title and in the mini bar instead. The endpoint has the same gate as saving progress (an episode of a show the profile follows: 404 / 403), and answers 502 when the chain doesn't end at a file. `PodcastStreamResolver` walks the chain by hand: up to 15 redirects, each hop asked for its first byte (`Range: bytes=0-0`, body never read) with the player's User-Agent, and checked like a feed address (`PodcastAddressRules`, shared with `PodcastManager`'s feed check), so an enclosure can't walk the server onto its own network. Android walks the chain in the player instead (`PodcastRedirects.kt`).

**Music and everything else get different `<video>` elements.** Music connects the element to the EQ's Web Audio graph (`ensureAudioGraph`), and an element can't be taken out of one. An episode is on another site with no CORS headers, so through the graph the browser hands the analyser zeros: a podcast started straight after a song played in silence while its time ran. `LiveRadioPlayer` keys its element `music` / `audio`, so switching between them makes a new one; one episode to the next, or one song to the next, keeps it.

## Plugins relevant to music

- `MusicBrainzArtworkProvider`, `FanartTvMusicArtworkProvider`, `TheAudioDbMusicArtworkProvider` — implement `IMusicArtworkProvider`. Surface in the metadata edit modal's image picker.
- `LrcLibLyricsProvider`, `GeniusLyricsProvider` — implement `ILyricsProvider`.
- `LastFmClientPlugin` — implements `ILastFmClient` and `IListeningDataProvider`.
- `DeezerContentRatingProvider` — implements `IMusicContentRatingProvider` (Clean / Explicit for untagged files).
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
