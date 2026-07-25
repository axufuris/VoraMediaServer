using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vora.Application.Artwork;
using Vora.Application.FileSystem;
using Vora.Application.Net;
using Vora.Application.Settings;

namespace Vora.Infrastructure.Artwork;

public class ArtworkThumbnailService : IArtworkThumbnailService
{
    private const string CustomArtworkPrefix = "/api/artwork/custom/";
    private const long MaxCacheBytes = 512L * 1024 * 1024;
    private const int PruneEveryWrites = 250;

    // Remote hosts we're willing to fetch and cache from. Local custom artwork
    // is always allowed. This keeps the anonymous endpoint from being an open
    // image proxy for arbitrary hosts.
    private static readonly HashSet<string> AllowedRemoteHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "image.tmdb.org",
        "artworks.thetvdb.com",
        "assets.fanart.tv",
    };

    private static int _writeCount;

    private readonly ISafeImageDownloader _downloader;
    private readonly ILogger<ArtworkThumbnailService> _logger;
    private readonly string _customArtworkPath;
    private readonly string _cachePath;

    public ArtworkThumbnailService(
        ISafeImageDownloader downloader,
        IOptions<StoragePathsOptions> storagePaths,
        ILogger<ArtworkThumbnailService> logger)
    {
        _downloader = downloader;
        _logger = logger;

        var configured = storagePaths.Value.CustomArtwork;
        _customArtworkPath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        _cachePath = Path.Combine(_customArtworkPath, "thumbs");
        Directory.CreateDirectory(_cachePath);
    }

    public async Task<string?> GetOrCreateThumbnailAsync(string src, int width, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;

        var cacheFile = Path.Combine(_cachePath, CacheKey(src, width) + ".jpg");
        if (File.Exists(cacheFile)) return cacheFile;

        byte[]? sourceBytes;
        try
        {
            sourceBytes = await LoadSourceAsync(src, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail source fetch failed for {Src}.", src);
            return null;
        }

        if (sourceBytes == null || sourceBytes.Length == 0) return null;

        try
        {
            using var image = Image.Load<Rgba32>(sourceBytes);
            if (image.Width > width)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(width, 0),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            var tempFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await image.SaveAsJpegAsync(tempFile, new JpegEncoder { Quality = 82 }, cancellationToken);
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail resize failed for {Src}.", src);
            return null;
        }

        if (Interlocked.Increment(ref _writeCount) % PruneEveryWrites == 0)
        {
            _ = Task.Run(PruneCache);
        }

        return File.Exists(cacheFile) ? cacheFile : null;
    }

    private async Task<byte[]?> LoadSourceAsync(string src, CancellationToken cancellationToken)
    {
        if (src.StartsWith(CustomArtworkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var fileName = src.Substring(CustomArtworkPrefix.Length);
            var localPath = SafePathResolver.ResolveContainedFilePath(_customArtworkPath, fileName);
            if (localPath == null || !File.Exists(localPath)) return null;
            return await File.ReadAllBytesAsync(localPath, cancellationToken);
        }

        if (Uri.TryCreate(src, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && AllowedRemoteHosts.Contains(uri.Host))
        {
            return await _downloader.DownloadAsync(src, cancellationToken);
        }

        return null;
    }

    private static string CacheKey(string src, int width)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{src}|{width}"));
        return Convert.ToHexString(bytes);
    }

    private void PruneCache()
    {
        try
        {
            var files = new DirectoryInfo(_cachePath).GetFiles("*.jpg");
            long total = files.Sum(f => f.Length);
            if (total <= MaxCacheBytes) return;

            var target = (long)(MaxCacheBytes * 0.8);
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= target) break;
                try { total -= file.Length; file.Delete(); }
                catch { /* another request may have removed it */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail cache prune failed.");
        }
    }
}
