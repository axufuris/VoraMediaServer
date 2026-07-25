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
    private const string DefaultKindFolder = "posters";

    private static readonly int[] WidthBuckets = { 200, 360, 500, 780, 1280 };

    private static readonly IReadOnlyDictionary<string, string> KindFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["poster"] = "posters",
        ["still"] = "stills",
        ["backdrop"] = "backdrops",
    };

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
    private readonly string _cacheRoot;

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

        _cacheRoot = Path.Combine(_customArtworkPath, "imagecache");
        foreach (var folder in KindFolders.Values.Distinct())
        {
            Directory.CreateDirectory(Path.Combine(_cacheRoot, folder));
        }
    }

    public async Task<string?> GetOrCreateThumbnailAsync(string src, int width, string kind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;

        var kindDir = Path.Combine(_cacheRoot, ResolveKindFolder(kind));
        var cacheFile = Path.Combine(kindDir, CacheKey(src, width) + ".jpg");
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

            Directory.CreateDirectory(kindDir);
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

    public void RemoveThumbnailsForSource(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return;

        foreach (var folder in KindFolders.Values.Distinct())
        {
            var kindDir = Path.Combine(_cacheRoot, folder);
            foreach (var width in WidthBuckets)
            {
                var cacheFile = Path.Combine(kindDir, CacheKey(src, width) + ".jpg");
                if (!File.Exists(cacheFile)) continue;
                try { File.Delete(cacheFile); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove cached thumbnail {Path}.", cacheFile); }
            }
        }
    }

    private static string ResolveKindFolder(string? kind)
    {
        if (!string.IsNullOrWhiteSpace(kind) && KindFolders.TryGetValue(kind, out var folder)) return folder;
        return DefaultKindFolder;
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
            var root = new DirectoryInfo(_cacheRoot);
            if (!root.Exists) return;

            var files = root.GetFiles("*.jpg", SearchOption.AllDirectories);
            long total = files.Sum(f => f.Length);
            if (total <= MaxCacheBytes) return;

            var target = (long)(MaxCacheBytes * 0.8);
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= target) break;
                try { total -= file.Length; file.Delete(); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail cache prune failed.");
        }
    }
}
