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

    // Both halves used to stat per URL — 48 File.Exists inside
    // RemoveThumbnailsForSource plus one for the overlay file — so sweeping a
    // library of thousands of items meant hundreds of thousands of stat calls
    // against a bind-mounted volume. Each half now reads its directory once and
    // tests membership instead.
    public void SweepPhysicalOverlays(IEnumerable<string?> urls, CancellationToken cancellationToken = default)
    {
        var overlayDir = !string.IsNullOrWhiteSpace(_storagePaths.CustomArtwork)
            ? _storagePaths.CustomArtwork
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        var present = urls.Where(u => !string.IsNullOrEmpty(u)).ToList();
        if (present.Count == 0) return;

        _artworkThumbnails.RemoveThumbnailsForSources(present, cancellationToken);

        var overlayFiles = present
            .Where(u => u!.Contains(OverlayMarker, StringComparison.Ordinal))
            .Where(u => u!.StartsWith(CustomArtworkUrlPrefix, StringComparison.Ordinal))
            .Select(u => u!.Split('/').Last())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (overlayFiles.Count == 0) return;

        HashSet<string> onDisk;
        try
        {
            if (!Directory.Exists(overlayDir)) return;
            onDisk = new HashSet<string>(
                Directory.EnumerateFiles(overlayDir).Select(Path.GetFileName)!,
                StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list overlays in {OverlayDir}; leaving them in place.", overlayDir);
            return;
        }

        foreach (var fileName in overlayFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!onDisk.Contains(fileName)) continue;

            var physicalPath = Path.Combine(overlayDir, fileName);
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
