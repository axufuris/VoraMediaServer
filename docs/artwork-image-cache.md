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
- **Width buckets**: `{200, 360, 500, 780, 1280}`. `kind` ∈ `poster | still | backdrop`; an unknown kind falls back to `posters`.
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

- **Resolution & video** come from the **best part** — the `MediaPart` with the highest `ParseResolutionHeight`. If a 4K file is added alongside a 1080p one, the badge upgrades to 4K.
- **Best audio** is chosen across **all** parts, ranked by `AudioQualityTier` (lossless/Atmos = 3, surround/6-channel = 2, base = 1), then by channel count — so the badge shows the best track available in any version, not just the best part's audio.
- **Regeneration gate**: overlays only regenerate when `MediaItem.LastOverlayGeneratedAt` is null, metadata is newer, or the template changed (`GetItemsPendingOverlayGenerationAsync`). Adding a part bumps none of those, so `AddMediaPartAsync` explicitly clears `LastOverlayGeneratedAt` to force a regen against the new best part (see `docs/scanning-and-tasks.md`).
- **Cleanup**: `CleanupOldOverlay` evicts the previous overlay's resized caches via `RemoveThumbnailsForSource` so a replaced overlay leaves nothing behind in `imagecache/`.
