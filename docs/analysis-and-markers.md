# Media analysis & playback markers

This doc covers how Vora locates intros, end credits, mid- and post-credits scenes ("stingers"), and (for TV) recaps and "next time on" previews, plus how those markers reach the player.

## Data model

`MediaItemAnalysis` holds **only** the file's measured `Duration` (a TimeSpan). Duration has three writers, in priority order: TMDB runtime (set during metadata mapping as a fallback before any file is probed), ffprobe of the primary file (overwrites TMDB unless the `Duration` lock is set on the item), and nothing else — the silence-detection pass does **not** touch Duration anymore.

All marker positions live in `MediaItemMarker`:

```
Id (Guid), MediaItemId (Guid, FK + cascade), Type (enum string), Start (TimeSpan), End (TimeSpan), Order (int)
```

`MarkerType` is `Intro | Recap | Preview | Credits | CreditsScene`. `Order` is `0` for everything except `CreditsScene`, where it's `1, 2, ...` in time order. A composite index sits on `(MediaItemId, Type, Order)`.

## Detection pipeline

`MediaAnalyzerManager.TriggerMediaItemSilenceDetectionAsync` runs the pipeline. Trigger sources: schedule (`DetectionTrigger.OnSchedule` / `OnAdditionAndSchedule`), media-add hook, or an admin "Analyze media" click on the per-item page (always force-runs regardless of the trigger setting).

Detection runs as a **tiered fallback**: the cheapest authoritative source wins, and only when it can't produce the wanted markers does the pipeline fall through to the next tier.

- **Tier 1 — embedded chapters** (`FFmpegAnalyzerService.ReadChaptersAsync` → `ChapterMarkerMapper`). A metadata-only `ffprobe -show_chapters` (no decode). When a release ships named intro/credits chapters ("Opening Titles", "End Credits", "Previously On", "Next Episode Preview") they're mapped to markers by title keyword and used as-is, skipping the FFmpeg decode entirely. Chapters are only trusted when they cover **every** enabled marker type (`ChapterMarkerResult.Covers`); a partial cover, unnamed scene chapters ("Chapter 1"…), or no chapters falls through to Tier 3. Intro/recap chapters must sit in the first 8 min and credits/preview chapters in the last 60% — an out-of-place title is ignored. "Opening credits" classifies as intro (not the credits roll); "next episode preview" as preview.
- **Tier 2 — audio fingerprinting** (`MediaAnalyzerManager.ComputeSeasonFingerprintIntrosAsync` → `AudioIntroDetector`, **intros only**, Jellyfin Intro-Skipper style). Runs once per season before the per-episode fan-out: each episode's head window (first 10 min) is fingerprinted with Chromaprint (`fpcalc`, via `FFmpegAnalyzerService.ExtractAudioFingerprintAsync`), fingerprints are cached in `MediaItemAudioFingerprint` (keyed by a file-size identity, so re-runs skip extraction), then `AudioFingerprintComparer` cross-correlates every episode against its siblings and `AudioIntroDetector` keeps an intro only where a **quorum** of siblings agree on its location. Needs ≥ 3 episodes; failures fall back silently. This catches intros silence/black can't — music over the theme, non-black title cards. Credits are **not** fingerprinted (outros vary too much); they stay on Tier 3. Per-episode, `MarkerMerge` applies precedence **chapters > fingerprint (intro) > silence/black**. On a **non-forced** run `RunSeasonSilenceDetectionAsync` first calls `IMediaRepository.SeasonHasPendingMarkerWorkAsync`; when every episode is already analyzed (or marker-locked) it skips this whole audio-fingerprint decode plus the per-episode fan-out — both would only be discarded — and jumps straight to `FinalizeSeasonMarkersAsync`. So re-running "Analyze library" over an already-analyzed library costs a cheap per-season query instead of re-decoding every season's audio. A forced run (Re-analyze all) bypasses the gate.
- **Tier 3 — silence/black detection** (the pass below). The universal fallback. When a fingerprint intro was found for an episode, the silence/black **head** decode is skipped (`SilenceDetectionParameters.SkipHeadWindow`) — that's the fingerprint tier's performance win: the head video decode is replaced by the cheaper audio fingerprint. Only the tail (credits/preview) is decoded. Recap detection needs the head, so it's forgone for fingerprint-intro episodes (a named recap chapter still yields one via Tier 1).

The silence/black tier per file is:

1. **Noise-floor probe** (`FFmpegAnalyzerService.ProbeMeanVolumeDbAsync`) — runs `ffmpeg -af volumedetect -vn -sn` and parses `mean_volume` from stderr.
2. **Dynamic silence threshold** — `threshold = mean_volume + SilenceThresholdOffsetDb` (default offset −12 dB; admin-tunable). If the probe fails it falls back to a fixed −40 dB.
3. **Joint silence + black-frame pass** (`AnalyzeSilenceDetectionsAsync`) — one ffmpeg invocation with both `silencedetect=noise={threshold}dB:d={minSilence}` and `blackdetect=d={minBlack}:pix_th=0.10`. stderr is parsed into two parallel interval lists.
4. **Marker assembly** (`MarkerAssembler`) — see below.
5. **Atomic replace** (`MediaRepository.ReplaceMarkersAsync`) — old markers for the item are deleted and the new set inserted in one transaction.
6. **SignalR push** (`MediaAnalysisUpdated`) — clients on the affected page (player, details page) refresh.

For a TV show or season, every episode runs the per-file pipeline, then a **season post-pass** clusters intro-end and credits-start timestamps across the season and snaps in-cluster episodes to the median.

## Marker assembly (movies)

Inputs: the silence intervals, the black-frame intervals, the file's `Duration`, and the TMDB `HasMidCreditsStinger` / `HasPostCreditsStinger` flags (already populated by `TmdbMetadataProvider` from keywords 179431 / 179430).

The algorithm joins silence and black intervals: a "joint gap" is a stretch where audio is below threshold **and** the frame is black. A joint gap is a stronger scene-boundary signal than either alone — it filters out a music-under-credits roll (audio not silent) and dim shots (audio silent but not black).

Markers emitted:

- **Intro** — `[0, jointGap.End]` for the first joint gap inside the first 8 minutes.
- **Credits** — `[jointGap.Start, Duration]` for the first joint gap whose start is ≥ 60% of `Duration`.
- **CreditsScene** — between consecutive joint gaps inside the credits region, where the gap-to-gap shot is ≥ 8 s and starts > 3 s after credits begin. Capped at `(HasMidCreditsStinger ? 1 : 0) + (HasPostCreditsStinger ? 1 : 0)`. If TMDB says zero stingers, all detected candidates are discarded as false positives.

## Marker assembly (TV)

Same Intro/Credits logic, plus:

- **Recap** — if there's a joint gap **before** the intro inside the first 90 seconds, that gap's end becomes the recap end (`[0, gap.End]`).
- **Preview** — first scene-like stretch (≥ 8 s, > 3 s past credits start) inside the credits region. Falls back to the trailing post-credits tail if no embedded scene is found.

The state machine treats `Preview` exactly like `CreditsScene`: a "Skip to Preview" button when one is upcoming.

## Season post-pass

After every episode in a season is processed, `FinalizeSeasonMarkersAsync` clusters per-episode markers. Settings:

- `EpisodeIntroClusterToleranceSec` (default 5) — ± window for "same time"
- `EpisodeIntroClusterMinAgreementPct` (default 70) — minimum % of episodes that must agree before clustering wins

For each of `Intro.End` and `Credits.Start`: take the median timestamp across the season; if ≥ N % of episodes are within tolerance of the median, the median wins. Each episode's marker is snapped to the median **only if** that episode is itself in the cluster. Outliers (recap-heavy openers, finales with extended credits) keep their individually-detected values.

The post-pass runs as an in-memory pass plus at most N small writes per season — cheap compared to the per-episode ffmpeg passes.

## Manual edits & locks

Admins can edit markers by hand from the per-item ⋯ menu → "Edit markers" (`MarkerEditorModal`). The modal loads the current markers + lock state, lets you add/edit/delete markers, change types, set start/end as HH:MM:SS, and assign `Order` to `CreditsScene` markers. Saves go through `PUT /api/media/{id}/markers`, which atomically replaces the marker set via `ReplaceMarkersAsync` and fires `MediaAnalysisUpdated` so any open page refreshes live.

The lock mechanism reuses the existing `LockableEntity.LockedFields` list: when `"Markers"` is present, automatic re-analysis skips that item. Specifically `MediaAnalyzerManager.RunMediaItemSilenceDetectionAsync` and `FinalizeSeasonMarkersAsync` both check `AreMarkersLockedAsync` early and bail out, so neither per-item detection nor the season cluster snap can overwrite manual edits. Toggle the lock from the editor — `PUT /api/media/{id}/markers/lock`. Admin "Analyze media" clicks still force-run, but they respect the lock too; admins must unlock first if they really want to re-detect a locked item.

## Player UX

The player loads markers when `currentMedia.id` changes (from `PlayableMedia.skipMarkers` if pre-loaded, otherwise via `GET /api/media/{id}/markers`). It listens to `MediaAnalysisUpdated` and refetches when the current item is the target.

The contextual skip prompt is derived from `(currentTime, markers, playbackPrefs)`:

| Current position | Button | Action |
| --- | --- | --- |
| Inside `Intro` | Skip Intro | seek to `Intro.End` |
| Inside `Recap` | Skip Recap | seek to `Recap.End` |
| Inside `CreditsScene` or `Preview` | *(none — let the user watch)* | — |
| Inside `Credits` with an upcoming scene ≥ `MinimumCreditsSceneSeconds` away | Skip to Scene / Skip to Preview | seek to that scene's `Start` |
| Inside `Credits` with no upcoming scene | Skip Credits | seek to near the end (`duration − 2`) |

Auto-skip honors per-profile flags `AutoSkipIntro` / `AutoSkipCredits`. **`Skip to Scene` is never auto-skipped** — that would silently bypass the thing the user wanted to see.

The seekbar paints marker bands inline: dim white for Intro/Recap, blue for the Credits roll, accent color for `CreditsScene` / `Preview`. Bands render under the progress fill so they show only on the unwatched portion.

## Settings reference

Per-profile (`UserProfile`):

- `AutoSkipIntro` / `AutoSkipCredits` (bool) — automatic seek when entering those markers
- `MinimumCreditsSceneSeconds` (int) — hide "Skip to Scene" when the next scene is within N seconds

Server-wide (`ServerSetting`):

- `SilenceThresholdOffsetDb` (int, default −12) — offset below mean volume that counts as silence
- `SilenceMinDurationMovieSec` / `SilenceMinDurationEpisodeSec` (double, defaults 1.5 / 1.0)
- `BlackFrameMinDurationSec` (double, default 0.5)
- `EpisodeIntroClusterToleranceSec` (int, default 5)
- `EpisodeIntroClusterMinAgreementPct` (int, default 70)

All six server fields are editable from Admin → Settings → Detection Tuning.

## API surface

Read (any authenticated user):

- `GET /api/media/{id}/markers` → `MediaMarkerVM[]` — player fetch
- `MediaDetailsVM.Markers` — included in the regular details fetch so the details page has markers without a second round-trip
- `GET / PUT /api/users/profiles/me/playback-preferences` → `PlaybackPreferencesVM` — per-profile auto-skip flags

Admin-only:

- `PUT /api/media/{id}/markers` — atomically replace the marker set for an item
- `GET / PUT /api/media/{id}/markers/lock` → `MarkersLockDto` — read or toggle the "Markers" lock
- `POST /api/media/{id}/analyze` — force-run detection on one item (respects the lock)
- `POST /api/libraries/{id}/analyze` — queue detection for every playable item in a library (respects the lock per item)
- `GET /api/libraries/{id}/marker-coverage` → `MarkerCoverageVM` — counts by marker type across Movies + Episodes in the library

## Admin operations

- **Admin → Settings → "Detection Tuning"** — edit the six server-wide tuning knobs.
- **Library admin page → "Analyze library"** — queue marker detection across every playable item in the library. Respects per-item locks; admin click force-runs the detection trigger gate.
- **Library admin page → "Marker coverage" card** — counts of items with each marker type, plus a "Missing duration" tile that turns red when ffprobe data is needed before assembly can run. Counts are scoped to playable items (Movies + Episodes); they don't include TvShows or Seasons. Click Refresh after a library analyze to see updated numbers — the dashboard does not auto-poll.
- **Media details page → ⋯ menu → "Analyze media"** — force-runs the per-item pipeline regardless of `RunDetections` mode. Cascades for shows and seasons. Still respects the per-item lock.
- **Media details page → ⋯ menu → "Edit markers"** — opens the manual editor modal. Use the Lock button to protect manual edits from being overwritten by future auto-detection runs.
- After analysis or manual edits complete, `MediaAnalysisUpdated` SignalR fires and any open details page or active player refreshes markers live.
