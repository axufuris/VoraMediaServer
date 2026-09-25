using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.FileSystem;
using Vora.Application.Settings;

namespace Vora.Application.Playlists;

public interface IPlaylistImageStore
{
    // The public url of the saved image, or null when the bytes are not a
    // PNG, JPEG or WebP image.
    Task<string?> SaveAsync(Guid playlistId, byte[] bytes);

    // Removes a file this store wrote. Anything else - a provider's url, a
    // media item's artwork - is left alone.
    void Delete(string? imageUrl);
}

// A playlist's cover sits beside the other custom artwork and is served by the
// same route, /api/artwork/custom/{file}.
public class PlaylistImageStore : IPlaylistImageStore
{
    private const string UrlPrefix = "/api/artwork/custom/";
    private const string FilePrefix = "playlist_";

    // The artwork route serves only these, so a GIF saved here could never load.
    private static readonly HashSet<string> ServableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    private readonly string _basePath;
    private readonly ILogger<PlaylistImageStore> _logger;

    public PlaylistImageStore(IOptions<StoragePathsOptions> storagePaths, ILogger<PlaylistImageStore> logger)
    {
        _logger = logger;
        var configured = storagePaths.Value.CustomArtwork;
        _basePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string?> SaveAsync(Guid playlistId, byte[] bytes)
    {
        var ext = ImageContentValidator.DetectImageExtension(bytes);
        if (ext == null || !ServableExtensions.Contains(ext)) return null;

        var fileName = $"{FilePrefix}{playlistId:N}_{Guid.NewGuid():N}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(_basePath, fileName), bytes);
        return UrlPrefix + fileName;
    }

    public void Delete(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith(UrlPrefix + FilePrefix, StringComparison.Ordinal)) return;

        var path = SafePathResolver.ResolveContainedFilePath(_basePath, imageUrl[UrlPrefix.Length..]);
        if (path == null) return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete playlist image {Path}.", path);
        }
    }
}
