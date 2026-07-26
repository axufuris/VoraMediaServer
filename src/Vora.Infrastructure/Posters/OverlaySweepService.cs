using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.Posters;
using Vora.Application.Settings;

namespace Vora.Infrastructure.Posters;

public class OverlaySweepService : IOverlaySweepService
{
    private const string OverlayMarker = "_overlay_";
    private const string CustomArtworkUrlPrefix = "/api/artwork/custom/";

    private readonly StoragePathsOptions _storagePaths;
    private readonly IArtworkThumbnailService _artworkThumbnails;
    private readonly ILogger<OverlaySweepService> _logger;

    public OverlaySweepService(IOptions<StoragePathsOptions> storagePaths, IArtworkThumbnailService artworkThumbnails, ILogger<OverlaySweepService> logger)
    {
        _storagePaths = storagePaths.Value;
        _artworkThumbnails = artworkThumbnails;
        _logger = logger;
    }

    public void SweepPhysicalOverlays(IEnumerable<string?> urls)
    {
        var overlayDir = !string.IsNullOrWhiteSpace(_storagePaths.CustomArtwork)
            ? _storagePaths.CustomArtwork
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        foreach (var url in urls)
        {
            if (string.IsNullOrEmpty(url)) continue;

            _artworkThumbnails.RemoveThumbnailsForSource(url);

            if (!url.Contains(OverlayMarker, StringComparison.Ordinal)) continue;
            if (!url.StartsWith(CustomArtworkUrlPrefix, StringComparison.Ordinal)) continue;

            var fileName = url.Split('/').Last();
            var physicalPath = Path.Combine(overlayDir, fileName);
            if (!File.Exists(physicalPath)) continue;

            try
            {
                File.Delete(physicalPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete overlay file at {PhysicalPath}.", physicalPath);
            }
        }
    }
}
