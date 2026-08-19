using Microsoft.Extensions.DependencyInjection;
﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Posters;

public interface IPosterOverlayManager
{
    Task<bool> RunLibraryOverlaySyncAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<bool> GenerateOverlaysForMediaAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
    Task<bool> HasPendingOverlayWorkAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<int> SweepOrphanedOverlayFilesAsync(CancellationToken cancellationToken = default);
}

public class PosterOverlayManager : IPosterOverlayManager
{
    private readonly IMediaRepository _mediaRepo;
    private readonly IOverlayTemplateRepository _templateRepo;
    private readonly IEnumerable<IOverlayProvider> _providers;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITaskProgressReporter _progress;
    private readonly Vora.Application.Artwork.IArtworkThumbnailService _thumbnails;
    private readonly ILogger<PosterOverlayManager> _logger;
    private readonly string _overlayDirectory;
    private readonly string _originalArtworkCacheDir;

    private readonly IServiceScopeFactory _scopeFactory;

    public PosterOverlayManager(
        IMediaRepository mediaRepo,
        IOverlayTemplateRepository templateRepo,
        IEnumerable<IOverlayProvider> providers,
        IOptions<StoragePathsOptions> storagePaths,
        IHttpClientFactory httpClientFactory,
        ITaskProgressReporter progress,
        Vora.Application.Artwork.IArtworkThumbnailService thumbnails,
        IServiceScopeFactory scopeFactory,
        ILogger<PosterOverlayManager> logger)
    {
        _mediaRepo = mediaRepo;
        _templateRepo = templateRepo;
        _providers = providers;
        _httpClientFactory = httpClientFactory;
        _progress = progress;
        _thumbnails = thumbnails;
        _scopeFactory = scopeFactory;
        _logger = logger;

        var configPath = storagePaths.Value.CustomArtwork;
        _overlayDirectory = !string.IsNullOrWhiteSpace(configPath) ? configPath : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");
        if (!Directory.Exists(_overlayDirectory)) Directory.CreateDirectory(_overlayDirectory);

        var originalCachePath = storagePaths.Value.OriginalArtworkCache;
        _originalArtworkCacheDir = !string.IsNullOrWhiteSpace(originalCachePath)
            ? originalCachePath
            : Path.Combine(AppContext.BaseDirectory, "Storage", "OriginalArtworkCache");
        if (!Directory.Exists(_originalArtworkCacheDir)) Directory.CreateDirectory(_originalArtworkCacheDir);
    }

    public async Task<bool> HasPendingOverlayWorkAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        if (await _templateRepo.AnyTemplateExistsForLibraryAsync(libraryId)) return true;

        return await _mediaRepo.AnyItemHasOverlayAppliedAsync(libraryId);
    }

    public async Task<int> SweepOrphanedOverlayFilesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_overlayDirectory)) return 0;

        var referenced = await _mediaRepo.GetReferencedOverlayFileNamesAsync();

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(_overlayDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = System.IO.Path.GetFileName(path);
            if (!fileName.Contains("_overlay_", StringComparison.Ordinal)) continue;
            if (referenced.Contains(fileName)) continue;

            _thumbnails.RemoveThumbnailsForSource($"/api/artwork/custom/{fileName}");

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete orphaned overlay file at {Path}.", path);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Swept {Count} orphaned poster-overlay file(s) from {Directory}.", deleted, _overlayDirectory);
        }

        return deleted;
    }

    public async Task<bool> RunLibraryOverlaySyncAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        var templates = await _templateRepo.GetTemplatesForLibraryAsync(libraryId);

        if (!templates.Any() && libraryId != Guid.Empty)
        {
            templates = await _templateRepo.GetTemplatesForLibraryAsync(Guid.Empty);
        }

        if (!templates.Any())
        {
            var itemsToRevert = await _mediaRepo.GetItemsPendingOverlayGenerationAsync(libraryId, DateTime.UtcNow);
            foreach (var item in itemsToRevert.Where(m => !string.IsNullOrEmpty(m.OriginalPosterUrl)))
            {
                var episodeBackdropWasOverlay = item is Episode && item.BackgroundUrl == item.PosterUrl;
                CleanupOldOverlay(item.PosterUrl, item.OriginalPosterUrl);
                item.PosterUrl = item.OriginalPosterUrl;

                if (episodeBackdropWasOverlay) item.BackgroundUrl = null;

                item.LastOverlayGeneratedAt = null;
                await _mediaRepo.UpdateMediaItemAsync(item);
            }
            return true;
        }

        var activeProvider = _providers.FirstOrDefault();
        if (activeProvider == null) return false;

        var itemsToProcess = await _mediaRepo.GetItemsPendingOverlayGenerationAsync(libraryId, templates.Select(t => t.UpdatedAt).Max());

        // Overlay generation downloads a poster + composites with ImageSharp per
        // item, so run several at once. Each item enriches in its own scope
        // (GenerateOverlaysForMediaAsync loads + saves in a fresh DbContext), and
        // items are independent (distinct rows + distinct output files).
        var total = itemsToProcess.Count;
        var done = 0;
        var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);
        await Parallel.ForEachAsync(
            itemsToProcess,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (item, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var overlay = scope.ServiceProvider.GetRequiredService<IPosterOverlayManager>();
                    await overlay.GenerateOverlaysForMediaAsync(item.Id, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate overlay for {Title} ({MediaItemId}).", item.Title, item.Id);
                }

                var n = Interlocked.Increment(ref done);
                _progress.Report($"Overlaying {item.Title} ({n}/{total})");
            });

        return true;
    }

    public async Task<bool> GenerateOverlaysForMediaAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        var item = await _mediaRepo.GetForPosterOverlayAsync(mediaItemId);
        if (item == null) return false;

        var templates = await _templateRepo.GetTemplatesForLibraryAsync(item.LibraryId);

        if (!templates.Any())
        {
            templates = await _templateRepo.GetTemplatesForLibraryAsync(Guid.Empty);
        }

        if (!templates.Any()) return true;

        var activeProvider = _providers.FirstOrDefault();
        if (activeProvider == null) return false;

        try
        {
            await ProcessSingleItemAsync(item, templates, activeProvider, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate overlay for {MediaItemId}.", item.Id);
            return false;
        }
    }

    private async Task ProcessSingleItemAsync(MediaItem item, IEnumerable<Vora.Domain.Entities.Posters.OverlayTemplate> templates, IOverlayProvider activeProvider, CancellationToken cancellationToken = default)
    {
        string mediaType = item switch
        {
            Movie => "Movie",
            TvShow => "TvShow",
            Season => "Season",
            Episode => "Episode",
            _ => item.GetType().Name
        };

        var template = templates.FirstOrDefault(t => t.TargetMediaType == mediaType);

        if (template == null || string.IsNullOrWhiteSpace(template.ConfigurationJson) || template.ConfigurationJson == "[]")
        {
            if (item.LastOverlayGeneratedAt != null && !string.IsNullOrEmpty(item.OriginalPosterUrl))
            {
                var episodeBackdropWasOverlay = item is Episode && item.BackgroundUrl == item.PosterUrl;
                CleanupOldOverlay(item.PosterUrl, item.OriginalPosterUrl);
                item.PosterUrl = item.OriginalPosterUrl;

                if (episodeBackdropWasOverlay) item.BackgroundUrl = null;

                item.LastOverlayGeneratedAt = null;
                await _mediaRepo.UpdateMediaItemAsync(item);
            }
            return;
        }

        if (string.IsNullOrEmpty(item.OriginalPosterUrl))
        {
            item.OriginalPosterUrl = item.PosterUrl;
        }

        if (string.IsNullOrEmpty(item.OriginalPosterUrl)) return;

        string? physicalSourcePath = await EnsureLocalArtworkAsync(item.OriginalPosterUrl, cancellationToken);

        if (string.IsNullOrEmpty(physicalSourcePath) || !File.Exists(physicalSourcePath)) return;

        // Badge attributes come from the best (highest-resolution) part, not an
        // arbitrary one — so when a 4K file is added alongside an existing 1080p,
        // the resolution/video/audio badges reflect the 4K version.
        var bestPart = item.MediaParts
            .OrderByDescending(p => ParseResolutionHeight(p.Resolution))
            .ThenBy(p => p.Id)
            .FirstOrDefault();
        var bestVideoTrack = bestPart?.VideoTracks?.FirstOrDefault();

        // The audio badge advertises the best audio the title has, across every
        // part (a lossless/Atmos track on a 1080p file still counts even if the
        // 4K only carries EAC3). Rank by quality tier first, then channels.
        var bestAudioTrack = item.MediaParts
            .SelectMany(p => p.AudioTracks)
            .OrderByDescending(AudioQualityTier)
            .ThenByDescending(a => a.Channels ?? 0)
            .FirstOrDefault();

        string? actualContentRating = item.ContentRating ?? await _mediaRepo.GetParentContentRatingAsync(item.Id);

        var dto = new OverlayMediaDto
        {
            Id = item.Id,
            MediaType = mediaType,
            ContentRating = actualContentRating,
            Resolution = bestPart?.Resolution,
            VideoFormat = bestVideoTrack?.HdrType,
            AudioCodec = bestAudioTrack?.Codec,
            HasStinger = item.HasMidCreditsStinger || item.HasPostCreditsStinger,
            Edition = item.Edition,
            ServerAdminRating = item.ServerAdminRating,
            ThirdPartyRating1Name = item.ThirdPartyRating1Name,
            ThirdPartyRating1 = item.ThirdPartyRating1,
            ThirdPartyRating2Name = item.ThirdPartyRating2Name,
            ThirdPartyRating2 = item.ThirdPartyRating2,
            PosterUrl = item.PosterUrl,
            OriginalPosterUrl = item.OriginalPosterUrl
        };

        var newPosterUrl = await activeProvider.GenerateOverlayAsync(dto, physicalSourcePath, template.ConfigurationJson, _overlayDirectory, cancellationToken);

        if (!string.Equals(newPosterUrl, item.PosterUrl, StringComparison.Ordinal))
        {
            CleanupOldOverlay(item.PosterUrl, item.OriginalPosterUrl);
            item.PosterUrl = newPosterUrl;
        }

        item.LastOverlayGeneratedAt = DateTime.UtcNow;

        await _mediaRepo.UpdateMediaItemAsync(item);
    }

    private async Task<string> EnsureLocalArtworkAsync(string urlOrPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return string.Empty;

        if (File.Exists(urlOrPath)) return urlOrPath;

        if (urlOrPath.StartsWith("/api/artwork/custom/", StringComparison.OrdinalIgnoreCase))
        {
            var routeFileName = urlOrPath.Split('/').Last();
            var physicalRoutePath = Path.Combine(_overlayDirectory, routeFileName);
            if (File.Exists(physicalRoutePath)) return physicalRoutePath;
        }

        if (!urlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var fetchUrl = UpgradeArtworkUrlForOverlay(urlOrPath);

        var uri = new Uri(fetchUrl);
        var safeName = string.Concat(uri.AbsolutePath.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-'));
        var localPath = Path.Combine(_originalArtworkCacheDir, safeName);

        if (File.Exists(localPath)) return localPath;

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var imageBytes = await httpClient.GetByteArrayAsync(fetchUrl, cancellationToken);
            await File.WriteAllBytesAsync(localPath, imageBytes, cancellationToken);

            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download artwork from {Url}.", fetchUrl);
            return string.Empty;
        }
    }

    private static string UpgradeArtworkUrlForOverlay(string url)
    {
        if (url.StartsWith("https://image.tmdb.org/t/p/", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = url.Substring("https://image.tmdb.org/t/p/".Length);
            var slashIndex = remainder.IndexOf('/');
            if (slashIndex > 0)
            {
                var sizeSegment = remainder.Substring(0, slashIndex);
                if (!string.Equals(sizeSegment, "original", StringComparison.OrdinalIgnoreCase))
                {
                    return $"https://image.tmdb.org/t/p/original{remainder.Substring(slashIndex)}";
                }
            }
        }

        return url;
    }

    private static int ParseResolutionHeight(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return 0;
        var digits = new string(resolution.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static int AudioQualityTier(Domain.Entities.Media.MediaAudioTrack track)
    {
        var codec = track.Codec?.ToLowerInvariant() ?? string.Empty;
        var title = track.Title?.ToLowerInvariant() ?? string.Empty;
        if (codec.Contains("truehd") || codec.Contains("dts-hd") || title.Contains("atmos")) return 3;
        if (codec.Contains("eac3") || codec.Contains("ac3") || (track.Channels ?? 0) >= 6) return 2;
        return 1;
    }

    private void CleanupOldOverlay(string? currentUrl, string? originalUrl)
    {
        if (string.IsNullOrWhiteSpace(currentUrl) || currentUrl == originalUrl) return;

        if (currentUrl.StartsWith("/api/artwork/custom/", StringComparison.OrdinalIgnoreCase) && currentUrl.Contains("_overlay_"))
        {
            _thumbnails.RemoveThumbnailsForSource(currentUrl);

            var oldFileName = currentUrl.Split('/').Last();
            var oldPhysicalPath = Path.Combine(_overlayDirectory, oldFileName);

            if (!File.Exists(oldPhysicalPath)) return;

            try
            {
                File.Delete(oldPhysicalPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old overlay file at {Path}.", oldPhysicalPath);
            }
        }
    }
}
