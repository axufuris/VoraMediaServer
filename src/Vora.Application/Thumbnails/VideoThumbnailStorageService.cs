using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Thumbnails;

public class VideoThumbnailStorageService : IVideoThumbnailStorageService
{
    private readonly ILogger<VideoThumbnailStorageService> _logger;

    public string RootDirectory { get; }

    public VideoThumbnailStorageService(IConfiguration configuration, ILogger<VideoThumbnailStorageService> logger)
    {
        _logger = logger;
        var configured = configuration["StoragePaths:VideoThumbnails"];
        RootDirectory = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "video-thumbnails");
        Directory.CreateDirectory(RootDirectory);
    }

    public string GetItemDirectory(Guid mediaItemId)
    {
        var id = mediaItemId.ToString("N");
        var shard = id[..2];
        return Path.Combine(RootDirectory, shard, id);
    }

    public string GetSpritePath(Guid mediaItemId) =>
        Path.Combine(GetItemDirectory(mediaItemId), "sprite.jpg");

    public string GetVttPath(Guid mediaItemId) =>
        Path.Combine(GetItemDirectory(mediaItemId), "thumbnails.vtt");

    public string GetVersionMarkerPath(Guid mediaItemId) =>
        Path.Combine(GetItemDirectory(mediaItemId), ".version");

    public void EnsureItemDirectory(Guid mediaItemId)
    {
        Directory.CreateDirectory(GetItemDirectory(mediaItemId));
    }

    public void DeleteItemDirectory(Guid mediaItemId)
    {
        try
        {
            var dir = GetItemDirectory(mediaItemId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete video thumbnail directory for media item {MediaItemId}", mediaItemId);
        }
    }

    public bool HasGeneratedAssets(Guid mediaItemId)
    {
        return File.Exists(GetSpritePath(mediaItemId)) && File.Exists(GetVttPath(mediaItemId));
    }
}
