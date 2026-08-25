# Artwork image cache & poster overlays

Two related server-side image subsystems. **This is not the scrub-bar thumbnail pipeline** — those are per-video sprite sheets covered in `docs/video-thumbnails.md`. This doc is about (1) the resized poster/still/backdrop cache the clients render, and (2) the badge overlays composited onto posters.

## Resized artwork cache

Clients never load full-resolution TMDB/TVDB/fanart images. They request a bucketed, server-resized copy through:

```
GET /api/artwork/thumb?src=<url|/api/artwork/custom/…>&w=<width>&kind=<poster|still|backdrop>
```

The endpoint (`Vora.Api/Endpoints/ArtworkEndpoints.cs`) is `AllowAnonymous` — same rationale as `/api/artwork/custom/{fileName}`: `<img>` tags can't carry a JWT. See the public-endpoint allowlist in `docs/backend-conventions.md`.

`IArtworkThumbnailService` / `ArtworkThumbnailService` (`Vora.Infrastructure/Artwork/`) does the work:

- **Resize** with ImageSharp — Lanczos3, `ResizeMode.Max` (only ever downscales, never upscales past the source width), saved as JPEG quality 82.
- **Width buckets**: `{200, 360, 500, 780, 1280, 1920}`. `kind` ∈ `poster | still | backdrop`; an unknown kind falls back to `posters`. `1920` exists for the full-bleed cinematic hero (`CinematicBackdrop intensity="hero"`); everything else tops out at `1280`. Because the stored TMDB backdrop is only `w1280`, a request for a bucket **above 1280** upgrades the download URL's TMDB size segment to `original` (`ResolveDownloadSource`) so there's real resolution to downscale from — otherwise a 1280 source stretched across a wide/4K hero looks pixelated. The cache key still uses the requested `src`, so nothing else needs to know about the upgrade.
- **Cache location**: `<StoragePaths:CustomArtwork>/imagecache/{posters,stills,backdrops}/`, one JPEG per entry named `SHA256(src|width).jpg`. It lives under the existing `CustomArtwork` mount — no new storage path or env var.
- **Source allowlist**: remote fetches are restricted to `image.tmdb.org`, `artworks.thetvdb.com`, `assets.fanart.tv`, plus local `/api/artwork/custom/` files. Anything else returns null (no fetch).
- **Size cap**: `MaxCacheBytes` = 512 MB. Every `PruneEveryWrites` (250) writes, a background prune deletes oldest-first (`LastWriteTimeUtc`) down to 80% of the cap. Users should expect the cache dir to grow to ~512 MB and self-bound there.

Frontend: use the `thumbUrl(src, width, kind)` helper (`src/Vora.Web/src/utils/thumbnails.ts`) to build the URL — don't hand-write `/api/artwork/thumb` query strings in components.

### Orphan cleanup

When a source image stops being referenced — poster/backdrop changed, item deleted, overlay replaced — its cached resizes must be evicted so the cap isn't wasted on dead entries. `IArtworkThumbnailService.RemoveThumbnailsForSource(src)` deletes every width bucket for that source. Current callers:

- `PosterOverlayManager` — when an overlay is regenerated, the old composited image's caches are dropped.
- `OverlaySweepService` — the background sweep that regenerates stale overlays.
- `MediaManager` — on item delete / poster or backdrop change.
- `MediaIngestionService`, `MetadataManager` — when metadata mapping replaces artwork.

Add a `RemoveThumbnailsForSource` call anywhere you swap or delete an image that could have been requested through `/api/artwork/thumb`.

## Poster overlays

`PosterOverlayManager` (`Vora.Application/Posters/`) composites quality badges onto a movie/show poster (resolution, video codec/range, best audio, ratings, edition). The composited result is itself served through the resized cache above.

- **Output format & size**: `LocalImageSharpOverlayProvider` composites onto a canvas capped at **1280 px wide** (`MaxCanvasWidthPx`, downscale-only — never upscales) and saves **JPEG quality 90** (`{id}_overlay_{key}.jpg`). This matches the largest resize bucket clients request, so there is no visible quality loss versus a full-res source (grid/rail posters are re-encoded to JPEG q82 at ≤500 px anyway). Overlays were historically lossless PNG upscaled to ≥1500 px, which made `CustomArtwork` balloon to tens of GB on large TV libraries. Bump `CacheKeyVersion`'s leading `vN` in the provider when the compositing output changes so stale cached files don't collide — and that same bump also **forces every already-overlaid item to re-generate once** (see the regeneration gate below), because `LayoutVersion` is derived from it.
- **Orphan sweep**: overlay files are the *live* posters (referenced by `MediaItem.PosterUrl`/`BackgroundUrl`), not a regenerable cache, so they can't be pruned oldest-first like `imagecache/` — evicting an in-use one would 404 a poster. Instead `SweepOrphanedOverlayFilesAsync` deletes only `*_overlay_*` files no `MediaItem` references (`IMediaRepository.GetReferencedOverlayFileNamesAsync`). It runs nightly via `QueueOverlayOrphanSweep` (enqueued from `ScheduledJobWorker`'s nightly-scan block) and also evicts each deleted file's resize caches.

- **Resolution & video** come from the **best part** — the `MediaPart` with the highest `ParseResolutionHeight`. If a 4K file is added alongside a 1080p one, the badge upgrades to 4K.
- **Best audio** is chosen across **all** parts, ranked by `AudioQualityTier` (lossless/Atmos = 3, surround/6-channel = 2, base = 1), then by channel count — so the badge shows the best track available in any version, not just the best part's audio.
- **Episodes have no portrait poster** — providers give an episode a 16:9 **still** (a Backdrop-kind artwork), which `MetadataMappingService` routes into `OriginalPosterUrl`/`PosterUrl` so the overlay badges it like any poster. It is **not** copied into the episode's `BackgroundUrl`: episode backdrops are left unset so `MediaDetailsVM` falls back to the show's widescreen backdrop (a badged poster made a poor backdrop). A backdrop a user sets themselves is preserved — the overlay revert and the `NullEpisodeBackgroundUrls` migration only clear an episode `BackgroundUrl` when it equals `PosterUrl` (the old overlay-clobbered value).
- **Regeneration gate**: overlays only regenerate when `MediaItem.LastOverlayGeneratedAt` is null, metadata is newer, the template changed, or **the item's `OverlayLayoutVersion` no longer matches the provider's current `LayoutVersion`** (`GetItemsPendingOverlayGenerationAsync`). That last clause is what makes a *layout/rendering* change (as opposed to a template-config change) roll out to every existing overlay: generation stamps `OverlayLayoutVersion = activeProvider.LayoutVersion`, and `LayoutVersion` is derived from `CacheKeyVersion`'s `vN`, so bumping the provider version re-generates all overlaid items once on the next sync (a `null` version — every item before this shipped — counts as stale). Adding a part bumps none of those, so `AddMediaPartAsync` explicitly clears `LastOverlayGeneratedAt` to force a regen against the new best part (see `docs/scanning-and-tasks.md`). The whole overlay step is skipped when no template is configured and nothing was ever overlaid — `RunFullLibraryWorkflowAsync` gates it on `HasPendingOverlayWorkAsync` so users who never touched overlays don't see the step run.
- **Cleanup**: `CleanupOldOverlay` evicts the previous overlay's resized caches via `RemoveThumbnailsForSource` so a replaced overlay leaves nothing behind in `imagecache/`. The no-template **revert** path in `RunLibraryOverlaySyncAsync` also calls `CleanupOldOverlay` before restoring `OriginalPosterUrl`, so deleting a template deletes its composited files instead of leaking them.
