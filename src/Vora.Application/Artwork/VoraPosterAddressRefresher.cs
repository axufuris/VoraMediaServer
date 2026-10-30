using Microsoft.Extensions.Logging;
using Vora.Application.Collections;

namespace Vora.Application.Artwork;

public interface IVoraPosterAddressRefresher
{
    Task<int> RefreshAsync();
}

public sealed class VoraPosterAddressRefresher(
    IVoraPosterCatalog catalog,
    ICollectionRepository collections,
    IArtworkThumbnailService thumbnails,
    ILogger<VoraPosterAddressRefresher> logger) : IVoraPosterAddressRefresher
{
    public async Task<int> RefreshAsync()
    {
        var current = new Dictionary<Guid, string>();
        var replaced = new List<string>();
        foreach (var row in await collections.GetPosterAddressesStartingWithAsync(VoraPosterAddress.Prefix))
        {
            if (catalog.Find(row.PosterUrl)?.Address is not { } address || address == row.PosterUrl) continue;

            current[row.CollectionId] = address;
            replaced.Add(row.PosterUrl);
        }

        if (current.Count == 0) return 0;

        await collections.SetPosterUrlsAsync(current);
        thumbnails.RemoveThumbnailsForSources(replaced);

        logger.LogInformation("Moved {Count} collection(s) to the current version of their Vora poster.", current.Count);
        return current.Count;
    }
}
