namespace Vora.Application.Artwork;

public interface IArtworkThumbnailService
{
    // Returns the absolute path to a cached, resized JPEG for the given source
    // (a local /api/artwork/custom/... path or a remote http(s) image URL),
    // generating and caching it on first request. Null if the source is invalid
    // or cannot be fetched.
    Task<string?> GetOrCreateThumbnailAsync(string src, int width, CancellationToken cancellationToken = default);
}
