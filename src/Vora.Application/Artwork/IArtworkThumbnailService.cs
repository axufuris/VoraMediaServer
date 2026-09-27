namespace Vora.Application.Artwork;

public interface IArtworkThumbnailService
{
    Task<string?> GetOrCreateThumbnailAsync(string src, int width, string kind, CancellationToken cancellationToken = default);

    void RemoveThumbnailsForSource(string? src);
    void RemoveThumbnailsForSources(IEnumerable<string?> sources, CancellationToken cancellationToken = default);
}
