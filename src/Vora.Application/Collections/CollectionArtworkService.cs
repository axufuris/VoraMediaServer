using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.FileSystem;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public interface ICollectionArtworkService
{
    Task<IEnumerable<CollectionArtworkVM>> GetArtworkAsync(Guid collectionId);
    Task<string> UploadAsync(Guid collectionId, UploadedFile file, ArtworkKind kind);
    Task<string> AddUrlAsync(Guid collectionId, string url, ArtworkKind kind);
    Task DeleteAsync(Guid artworkId);
    Task RefreshProviderArtworkAsync(Guid collectionId, string providerId);
    Task<IReadOnlyList<CollectionArtworkVM>> FindProviderArtworkAsync(string providerId, string title, CancellationToken cancellationToken);
}

public class CollectionArtworkService : ICollectionArtworkService
{
    private readonly ICollectionRepository _repository;
    private readonly IEnumerable<IArtworkProvider> _artworkProviders;
    private readonly IEnumerable<IMetadataProvider> _metadataProviders;
    private readonly ISafeImageDownloader _imageDownloader;
    private readonly ILogger<CollectionArtworkService> _logger;
    private readonly string _basePath;

    public CollectionArtworkService(ICollectionRepository repository, IOptions<StoragePathsOptions> storagePaths, IEnumerable<IArtworkProvider> artworkProviders, IEnumerable<IMetadataProvider> metadataProviders, ISafeImageDownloader imageDownloader, ILogger<CollectionArtworkService> logger)
    {
        _repository = repository;
        _artworkProviders = artworkProviders;
        _metadataProviders = metadataProviders;
        _imageDownloader = imageDownloader;
        _logger = logger;

        _basePath = StorageRoots.CustomArtwork(storagePaths.Value);

        if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);
    }

    public async Task<IEnumerable<CollectionArtworkVM>> GetArtworkAsync(Guid collectionId)
    {
        var artwork = await _repository.GetCollectionArtworkAsync(collectionId);
        return artwork.Select(ToViewModel);
    }

    private static CollectionArtworkVM ToViewModel(CollectionArtwork artwork) => new()
    {
        Id = artwork.Id.ToString(),
        IsUserUploaded = artwork.IsUserUploaded,
        Url = artwork.Url,
        Type = artwork.Kind.ToString(),
        Language = artwork.Language,
        Width = artwork.Width,
        Height = artwork.Height,
        VoteAverage = artwork.VoteAverage
    };

    public async Task<string> UploadAsync(Guid collectionId, UploadedFile file, ArtworkKind kind)
    {
        var ext = Path.GetExtension(file.FileName);
        var fileName = $"coll_{collectionId}_{kind.ToString().ToLower()}_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(_basePath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.Content.CopyToAsync(stream);
        }

        var publicUrl = $"/api/artwork/custom/{fileName}";

        await _repository.AddCollectionArtworkAsync(new CollectionArtwork
        {
            CollectionId = collectionId,
            Kind = kind,
            Url = publicUrl,
            ProviderId = "user_upload",
            IsUserUploaded = true,
            Language = "None"
        });

        return publicUrl;
    }

    public async Task<string> AddUrlAsync(Guid collectionId, string url, ArtworkKind kind)
    {
        var fileName = $"coll_{collectionId}_{kind.ToString().ToLower()}_{Guid.NewGuid()}.jpg";
        var filePath = Path.Combine(_basePath, fileName);

        var imageBytes = await _imageDownloader.DownloadAsync(url);
        await File.WriteAllBytesAsync(filePath, imageBytes);

        var publicUrl = $"/api/artwork/custom/{fileName}";

        await _repository.AddCollectionArtworkAsync(new CollectionArtwork
        {
            CollectionId = collectionId,
            Kind = kind,
            Url = publicUrl,
            ProviderId = "user_url",
            IsUserUploaded = true,
            Language = "None"
        });

        return publicUrl;
    }

    public async Task DeleteAsync(Guid artworkId)
    {
        var artwork = await _repository.GetCollectionArtworkByIdAsync(artworkId);
        if (artwork == null || !artwork.IsUserUploaded) return;

        if (artwork.Url.StartsWith("/api/artwork/custom/"))
        {
            var fileName = artwork.Url.Split('/').Last();
            var physicalPath = Path.Combine(_basePath, fileName);
            if (File.Exists(physicalPath)) File.Delete(physicalPath);
        }

        await _repository.DeleteCollectionArtworkAsync(artworkId);
    }

    public async Task RefreshProviderArtworkAsync(Guid collectionId, string providerId)
    {
        var collection = await _repository.GetForUpdateAsync(collectionId);
        if (collection == null || collection.TmdbId == 0) return;

        var provider = _artworkProviders.FirstOrDefault(p => p.Id == providerId);
        if (provider == null) return;

        var newArtwork = await LookUpAsync(provider, collectionId, collection.TmdbId, collection.Title, CancellationToken.None);
        await _repository.ReplaceProviderArtworkAsync(collectionId, newArtwork);
    }

    public async Task<IReadOnlyList<CollectionArtworkVM>> FindProviderArtworkAsync(string providerId, string title, CancellationToken cancellationToken)
    {
        var provider = _artworkProviders.FirstOrDefault(p => p.Id == providerId);
        if (provider == null || string.IsNullOrWhiteSpace(title)) return [];

        var found = await LookUpAsync(provider, Guid.Empty, null, title.Trim(), cancellationToken);
        return found.Select(ToViewModel).ToList();
    }

    private async Task<List<CollectionArtwork>> LookUpAsync(IArtworkProvider provider, Guid collectionId, int? tmdbId, string title, CancellationToken cancellationToken)
    {
        var tmdbCollectionId = tmdbId ?? await FindTmdbCollectionIdAsync(title, cancellationToken);
        var results = await provider.GetArtworkAsync(tmdbCollectionId?.ToString(), null, null, "Collection", null, title, cancellationToken);

        return results.Select(r => new CollectionArtwork
        {
            CollectionId = collectionId,
            Kind = (Vora.Domain.Enums.ArtworkKind)r.Kind,
            Url = r.Url,
            VoteAverage = r.VoteAverage,
            ProviderId = provider.Id,
            IsUserUploaded = false
        }).ToList();
    }

    private async Task<int?> FindTmdbCollectionIdAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var provider in _metadataProviders)
        {
            try
            {
                var id = await provider.FindCollectionIdAsync(title, cancellationToken);
                if (id != null) return id;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Looking up the TMDB collection named {Title} with {Provider} failed.", title, provider.Id);
            }
        }
        return null;
    }
}
