using Microsoft.Extensions.Logging;
using Vora.Domain.Entities.Library;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public interface ICollectionDescriptionService
{
    Task<bool> HasAwaitingAsync(Guid libraryId);
    Task<int> FillAwaitingAsync(Guid libraryId, CancellationToken cancellationToken);
    Task<string?> RefreshAsync(Guid collectionId, CancellationToken cancellationToken);
}

public class CollectionDescriptionService : ICollectionDescriptionService
{
    private readonly ICollectionRepository _repository;
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly ILogger<CollectionDescriptionService> _logger;

    public CollectionDescriptionService(ICollectionRepository repository, IEnumerable<IMetadataProvider> providers, ILogger<CollectionDescriptionService> logger)
    {
        _repository = repository;
        _providers = providers;
        _logger = logger;
    }

    public async Task<bool> HasAwaitingAsync(Guid libraryId) =>
        (await _repository.GetCollectionsAwaitingDescriptionAsync(libraryId)).Count > 0;

    public async Task<int> FillAwaitingAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var filled = 0;
        foreach (var target in await _repository.GetCollectionsAwaitingDescriptionAsync(libraryId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overview = await FetchAsync(target.TmdbId, cancellationToken);
            if (overview == null) continue;

            await _repository.UpdateDescriptionAsync(target.CollectionId, overview);
            filled++;
        }
        return filled;
    }

    public async Task<string?> RefreshAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetForUpdateAsync(collectionId);
        if (collection?.TmdbId is not int tmdbId) return null;
        if (collection.IsLocked(nameof(Collection.Description))) return collection.Description;

        var overview = await FetchAsync(tmdbId, cancellationToken);
        if (overview == null) return collection.Description;

        await _repository.UpdateDescriptionAsync(collectionId, overview);
        return overview;
    }

    private async Task<string?> FetchAsync(int tmdbId, CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            try
            {
                var overview = await provider.FetchCollectionOverviewAsync(tmdbId, cancellationToken);
                if (overview != null) return overview;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fetching the description for TMDB collection {TmdbId} from {Provider} failed.", tmdbId, provider.Id);
            }
        }
        return null;
    }
}

public class CollectionDescriptionResponse
{
    public string Description { get; set; } = string.Empty;
}
