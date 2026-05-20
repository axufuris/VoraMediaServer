using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public interface ICollectionArtworkService
{
    Task<IEnumerable<CollectionArtwork>> GetArtworkAsync(Guid collectionId);
    Task<string> UploadAsync(Guid collectionId, IFormFile file, ArtworkKind kind);
    Task<string> AddUrlAsync(Guid collectionId, string url, ArtworkKind kind);
    Task DeleteAsync(Guid artworkId);
    Task RefreshProviderArtworkAsync(Guid collectionId, string providerId);
}

public class CollectionArtworkService : ICollectionArtworkService
{
    private readonly ICollectionRepository _repository;
    private readonly IEnumerable<IArtworkProvider> _artworkProviders;
    private readonly string _basePath;

    public CollectionArtworkService(ICollectionRepository repository, IConfiguration config, IEnumerable<IArtworkProvider> artworkProviders)
    {
        _repository = repository;
        _artworkProviders = artworkProviders;

        var configPath = config["StoragePaths:CustomArtwork"];
        _basePath = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);
    }

    public async Task<IEnumerable<CollectionArtwork>> GetArtworkAsync(Guid collectionId)
    {
        return await _repository.GetCollectionArtworkAsync(collectionId);
    }

    public async Task<string> UploadAsync(Guid collectionId, IFormFile file, ArtworkKind kind)
    {
        var ext = Path.GetExtension(file.FileName);
        var fileName = $"coll_{collectionId}_{kind.ToString().ToLower()}_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(_basePath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
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

        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            var imageBytes = await httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, imageBytes);
        }

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

        var results = await provider.GetArtworkAsync(collection.TmdbId.ToString(), null, null, "Collection", null, collection.Title);

        var newArtwork = results.Select(r => new CollectionArtwork
        {
            CollectionId = collectionId,
            Kind = (Vora.Domain.Enums.ArtworkKind)r.Kind,
            Url = r.Url,
            VoteAverage = r.VoteAverage,
            ProviderId = provider.Id,
            IsUserUploaded = false
        }).ToList();

        await _repository.ReplaceProviderArtworkAsync(collectionId, newArtwork);
    }
}
