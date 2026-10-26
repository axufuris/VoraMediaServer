# Artwork image cache & poster overlays

Two related server-side image subsystems. **This is not the scrub-bar thumbnail pipeline** — those are per-video sprite sheets covered in `docs/video-thumbnails.md`. This doc is about (1) the resized poster/still/backdrop cache the clients render, and (2) the badge overlays composited onto posters.

## Clear logos keep their transparency

The cache buckets by kind (`posters` / `stills` / `backdrops` / `logos`), and the
**container follows the kind**: `logo` is written as `.png` and served as
`image/png`, everything else stays JPEG at quality 82.

This is not an optimisation, it is the whole feature. JPEG has no alpha channel,
and a clear logo stores its transparent background as RGB(0,0,0) with alpha 0 —
drop the alpha and those pixels become opaque black, so the wordmark arrives in a
black rectangle, as a perfectly healthy `200`. The decision lives in
`IsTransparentKind` / `CacheExtension` in `ArtworkThumbnailService` so the
save branch, the cache path and the eviction path cannot disagree.

Two matching details: `NormalizeKind` in `ArtworkEndpoints` must list `logo`,
because its fallback is `poster` and an unrecognised kind is silently cached as
a JPEG; and `RemoveThumbnailsForSource` deletes both `.jpg` and `.png` for a
source, since it is given no kind and a stale logo would otherwise outlive an
artwork change.

## Resized artwork cache

Clients never load full-resolution TMDB/TVDB/fanart images. They request a bucketed, server-resized copy through:

```
GET /api/artwork/thumb?src=<url|/api/artwork/custom/…>&w=<width>&kind=<poster|still|backdrop>
```

The endpoint (`Vora.Api/Endpoints/ArtworkEndpoints.cs`) is `AllowAnonymous` — same rationale as `/api/artwork/custom/{fileName}`: `<img>` tags can't carry a JWT. See the public-endpoint allowlist in `docs/backend-conventions.md`.

`IArtworkThumbnailService` / `ArtworkThumbnailService` (`Vora.Infrastructure/Artwork/`) does the work:

- **Resize** with ImageSharp — Lanczos3, `ResizeMode.Max` (only ever downscales, never upscales past the source width), saved as JPEG quality 82.
- **Width buckets**: `{200, 360, 500, 780, 1280, 1920}`. `kind` ∈ `poster | still | backdrop`; an unknown kind falls back to `posters`. `1920` exists for the full-bleed cinematic hero (`CinematicBackdrop intensity="hero"`); everything else tops out at `1280`. Because the stored TMDB backdrop is only `w1280`, a request for a bucket **above 1280** upgrades the download URL's TMDB size segment to `original` (`ResolveDownloadSource`) so there's real resolution to downscale from — otherwise a 1280 source stretched across a wide/4K hero looks pixelated. The other direction matters too: a stored `original` URL asked for at **1280 or less** is downloaded at the smallest TMDB size that covers the bucket (`w300`, `w500`, `w780`, `w1280`). TMDB originals can be larger than `SafeImageDownloader`'s 10 MB cap. Spider-Man: Brand New Day's logo is a 12.6 MB PNG, so every request for it failed and the details page showed a broken image where the title belonged; its `w780` copy is 194 KB. The cache key still uses the requested `src`, so nothing else needs to know about either swap. `DetailHero` also falls back to the text title when the logo fails to load for any other reason.
- **Cache location**: `<StoragePaths:CustomArtwork>/imagecache/{posters,stills,backdrops}/`, one JPEG per entry named `SHA256(src|width).jpg`. It lives under the existing `CustomArtwork` mount — no new storage path or env var.
- **Source allowlist**: remote fetches are restricted to `image.tmdb.org`, `artworks.thetvdb.com`, `assets.fanart.tv`, plus local `/api/artwork/custom/` files and `/api/artwork/local/{mediaItemId}/{fileName}` addresses, which `ILocalArtworkLocator` resolves to an image in that title's media folder (read in place, never copied; `docs/local-metadata.md`). Anything else returns null (no fetch).
- **Size cap**: `MaxCacheBytes` = 512 MB. Every `PruneEveryWrites` (250) writes, a background prune deletes oldest-first (`LastWriteTimeUtc`) down to 80% of the cap. Users should expect the cache dir to grow to ~512 MB and self-bound there.

Frontend: use the `thumbUrl(src, width, kind)` helper (`src/Vora.Web/src/utils/thumbnails.ts`) to build the URL — don't hand-write `/api/artwork/thumb` query strings in components.

### Third-party images over plain HTTP

Channel logos, station logos and podcast artwork come from hosts outside the allowlist, so the browser loads them directly (`MediaCard`'s `uncachedImage`). Many are stored as `http://`, and a browser will not load those into an HTTPS page: it retries over HTTPS, logs a mixed-content warning, and shows nothing when the host has no HTTPS. A site cannot opt out of that.

So on an HTTPS page those images go through the cache:

- **Web:** `secureImageUrl(url, kind)` (`utils/secureImageUrl.ts`) wraps every place one becomes an `<img src>` or a player `posterUrl`. On an HTTPS page an `http://` address becomes `thumbUrl(url, 500, kind)`; anything else, and everything on an HTTP page, is returned unchanged. `kind` is `'logo'` (kept as PNG) or `'poster'` for podcast covers (JPEG).
- **Server:** `ArtworkThumbnailService` fetches a source outside the allowlist only when `IRemoteImageRegistry.IsKnown(src)`. The registry (`Vora.Application/Artwork/RemoteImageRegistry.cs`, a singleton, `http://` addresses only, 250,000 entries then it starts over) holds the addresses the server has itself **sent to a client**. `/api/artwork/thumb` is anonymous, so without this it would fetch any public image for anyone.
- **How an address gets in:** the response property carries `[RemoteImage]`, and `RemoteImageJson.RememberRemoteImages` (added to the HTTP and MVC JSON options in `AddVoraJsonOptions`) records the value as it is written. A new view model that returns a third-party image address **must** carry the attribute, or its `http://` images 404 on an HTTPS page. Marked today: `IptvChannelVM.LogoUrl`, `IptvRecordingChannelVM.LogoUrl`, `SmartListEntryVM.ImageUrl`, and `ArtworkUrl` / `ShowArtworkUrl` on the five podcast view models.
- **Limits:** `SafeImageDownloader` still applies (public hosts only on every redirect hop, 10 MB, image bytes checked), so a logo served from a LAN address is not fetched. Eight third-party fetches run at once, and a source that failed is not retried for 15 minutes (`FailedRemoteImageRetryAfter`), so a page of dead logos does not hold eight connections open on every visit.
- **After a restart** the registry is empty until a client loads a list again. Images already in the cache are served regardless, because the cached file is checked first.

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
- **Orphan sweep**: overlay files are the *live* posters (referenced by `MediaItem.PosterUrl`/`BackgroundUrl`), not a regenerable cache, so they can't be pruned oldest-first like `imagecache/` — evicting an in-use one would 404 a poster. Instead `SweepOrphanedOverlayFilesAsync` deletes only `*_overlay_*` files no `MediaItem` references (`IMediaRepository.GetReferencedOverlayFileNamesAsync`) and that are older than 10 minutes — a per-item generation (a new file's ingest, say) writes its file before saving the row that references it, and the sweep doesn't share its key. It runs nightly via `QueueOverlayOrphanSweep` (enqueued from `ScheduledJobWorker`'s nightly-scan block) and also evicts each deleted file's resize caches.

- **Resolution & video** come from the **best part** — the `MediaPart` with the highest `ParseResolutionHeight`. If a 4K file is added alongside a 1080p one, the badge upgrades to 4K.
- **Best audio** is chosen across **all** parts, ranked by `AudioQualityTier` (lossless/Atmos = 3, surround/6-channel = 2, base = 1), then by channel count — so the badge shows the best track available in any version, not just the best part's audio.
- **Episodes have no portrait poster** — providers give an episode a 16:9 **still** (a Backdrop-kind artwork), which `MetadataMappingService` routes into `OriginalPosterUrl`/`PosterUrl` so the overlay badges it like any poster. It is **not** copied into the episode's `BackgroundUrl`: episode backdrops are left unset so `MediaDetailsVM` falls back to the show's widescreen backdrop (a badged poster made a poor backdrop). A backdrop a user sets themselves is preserved — the overlay revert and the `NullEpisodeBackgroundUrls` migration only clear an episode `BackgroundUrl` when it equals `PosterUrl` (the old overlay-clobbered value).
- **Regeneration gate**: overlays only regenerate when `MediaItem.LastOverlayGeneratedAt` is null, metadata is newer, the template changed, or **the item's `OverlayLayoutVersion` no longer matches the provider's current `LayoutVersion`** (`GetItemsPendingOverlayGenerationAsync`). That last clause is what makes a *layout/rendering* change (as opposed to a template-config change) roll out to every existing overlay: generation stamps `OverlayLayoutVersion = activeProvider.LayoutVersion`, and `LayoutVersion` is derived from `CacheKeyVersion`'s `vN`, so bumping the provider version re-generates all overlaid items once (a `null` version — every item before this shipped — counts as stale). **The server starts that sync itself**: on its first pass after startup `ScheduledJobWorker` asks `HasOverlaysFromAnOlderLayoutAsync` and, when any overlaid poster carries another layout version, queues Generate Poster Overlays for all libraries. Before, the redraw waited for a library scan, Update Library Now or the nightly overlay schedule, which is off by default, so a layout change such as the ratings keeping their slots could stay invisible. Adding a part bumps none of those, so `AddMediaPartAsync` explicitly clears `LastOverlayGeneratedAt` to force a regen against the new best part (see `docs/scanning-and-tasks.md`). A rating the overlay draws is the same: `MetadataMappingService.ApplyRatingsAsync` clears it whenever a library rating's value or source changes (a score OMDb fills in days later, one a provider drops), and an imported server rating clears it too (`docs/library-migration.md`). The Refresh Ratings task and a library scan both end with the overlay step, so those items are redrawn there. Overlays used to be redrawn only for metadata, template and layout changes, so posters drawn before their scores arrived never got them; `v9-redraw-with-current-ratings` redraws every overlay once for that. The whole overlay step is skipped when no template is configured and nothing was ever overlaid — `RunFullLibraryWorkflowAsync` gates it on `HasPendingOverlayWorkAsync` so users who never touched overlays don't see the step run.
- **Ratings cluster**: the composite ratings badge draws `ServerAdminRating` with the Vora mark (`Rating/Vora.png`, the V from the brand mark; it was a star until `v10-vora-mark-for-the-server-rating`), then `ThirdPartyRating1` and `ThirdPartyRating2`, which come from the library's Third Party Rating 1 and 2 sources. **Each rating has its own slot**: the server rating the top third of the box, rating 1 the middle, rating 2 the bottom (`ResolveRatingSlots`). A slot with no score, or with a source name `BadgeResolver` has no logo for, is left empty, and the ones below it stay where they are, so a rating sits in the same place on every poster. They used to be drawn from the top, so a title without a server rating showed its TMDb score where the others showed the Vora one; `v11-ratings-keep-their-slots` redraws every overlay once for that. The whole cluster is skipped when all three are missing. The template editor's three pickers only change its preview.
- **Cleanup**: `CleanupOldOverlay` evicts the previous overlay's resized caches via `RemoveThumbnailsForSource` so a replaced overlay leaves nothing behind in `imagecache/`. The no-template **revert** path in `RunLibraryOverlaySyncAsync` also calls `CleanupOldOverlay` before restoring `OriginalPosterUrl`, so deleting a template deletes its composited files instead of leaking them.

## Adding artwork by URL

"Add from URL" on the media and collection edit modals goes through `ISafeImageDownloader` (`Vora.Application/Net/SafeImageDownloader.cs`), which fetches a user-supplied URL from inside the server. That makes it an SSRF surface, so it enforces a scheme allowlist, resolves the host and refuses private/loopback/link-local addresses, caps the body at 10 MB, and times out at 15 s.

Two things follow from the guard that are easy to get wrong:

- **Redirects are followed by hand, not by `HttpClient`.** The named client sets `AllowAutoRedirect = false` on purpose: an automatic redirect would jump to the new host *without* re-running the private-address check, so a public URL could bounce the request onto an internal address. `SendFollowingRedirectsAsync` walks up to 5 hops and re-validates the scheme and host on each one. Don't "simplify" this by turning `AllowAutoRedirect` back on — CDN-backed artwork URLs do redirect, and refusing them outright is what made valid poster links fail.
- **The content type is checked, but the bytes decide.** A host that answers `application/octet-stream` or sends no type at all is treated as unknown rather than refused; a type that names a specific non-image is refused up front. Either way the body is sniffed for a real image signature (JPEG/PNG/GIF/BMP/WebP/ISO-BMFF), which is a stronger test than trusting the header — an HTML error page labelled `image/jpeg` is still rejected.

The endpoints bind the artwork type as **`[FromQuery] ArtworkKind kind`**. The web client builds that query in one place, `artworkKindQuery` in `api/Media/artworkService.ts`, used by `artworkService` and `collectionAdminService`. The edit modals previously posted `?type=` inline, which left the required parameter unbound, so every upload and add-from-URL was rejected by ASP.NET with a bare 400 before any Vora code ran — including the downloader above. `ArtworkUrlEndpointContractTests` pins the server side. Don't rename the server parameter to match a client: the OpenAPI document, and the native clients generated from it, use `kind`.

Failures raise `InvalidOperationException` with a specific message, which the global handler turns into a 400 carrying that message in `ProblemDetails.detail`. Client handlers should surface it with `errorDetail(err, "…")` from `utils/apiError.ts` rather than alerting a fixed string — otherwise a precisely-explained failure reaches the viewer as "Failed to add URL" and the only way to diagnose it is the container log.
