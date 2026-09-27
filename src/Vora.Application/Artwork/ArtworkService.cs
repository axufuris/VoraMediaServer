using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.FileSystem;
using Vora.Application.Media;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Artwork;

public interface IArtworkService
{
    Task<IEnumerable<MediaArtworkVM>> GetArtworkOptionsAsync(Guid mediaItemId);
    Task RefreshProviderArtworkAsync(Guid mediaItemId, string? providerId);
    Task<string> UploadAsync(Guid mediaItemId, UploadedFile file, ArtworkKind kind);
    Task<string> AddUrlAsync(Guid mediaItemId, string url, ArtworkKind kind);
    Task DeleteAsync(Guid artworkId);
}

public class ArtworkService : IArtworkService
{
    private const string CustomArtworkUrlPrefix = "/api/artwork/custom/";
    private const string UserUploadProvider = "user_upload";
    private const string UserUrlProvider = "user_url";

    private const string DefaultArtworkProvider = "tmdb_artwork";

    private readonly IMediaArtworkRepository _repository;
    private readonly IMediaRepository _mediaRepository;
    private readonly IEnumerable<IArtworkProvider> _artworkProviders;
    private readonly ISafeImageDownloader _imageDownloader;
    private readonly ILogger<ArtworkService> _logger;
    private readonly string _basePath;

    public ArtworkService(
        IMediaArtworkRepository repository,
        IMediaRepository mediaRepository,
        IEnumerable<IArtworkProvider> artworkProviders,
        IOptions<StoragePathsOptions> storagePaths,
        ISafeImageDownloader imageDownloader,
        ILogger<ArtworkService> logger)
    {
        _repository = repository;
        _mediaRepository = mediaRepository;
        _artworkProviders = artworkProviders;
        _imageDownloader = imageDownloader;
        _logger = logger;

        var configPath = storagePaths.Value.CustomArtwork;
        _basePath = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public async Task<IEnumerable<MediaArtworkVM>> GetArtworkOptionsAsync(Guid mediaItemId)
    {
        var artwork = await _repository.GetMediaArtworkAsync(mediaItemId);
        return artwork.Select(a => new MediaArtworkVM
        {
            Id = a.Id.ToString(),
            IsUserUploaded = a.IsUserUploaded,
            Url = a.Url,
            Type = a.Kind.ToString(),
            Language = a.Language,
            Width = a.Width,
            Height = a.Height,
            VoteAverage = a.VoteAverage
        });
    }

    public async Task RefreshProviderArtworkAsync(Guid mediaItemId, string? providerId)
    {
        var item = await _mediaRepository.GetForMetadataSyncAsync(mediaItemId);
        if (item == null) return;

        var resolvedProviderId = !string.IsNullOrWhiteSpace(providerId)
            ? providerId
            : (item.Library?.ArtworkProviderId ?? DefaultArtworkProvider);

        var provider = _artworkProviders.FirstOrDefault(p => p.Id == resolvedProviderId)
            ?? _artworkProviders.FirstOrDefault(p => p.Id == DefaultArtworkProvider);
        if (provider == null) return;

        var results = await provider.GetArtworkAsync(item.TmdbId, item.TvdbId, item.ImdbId, item.GetType().Name, null, item.Title);

        var newArtwork = results.Select(r => new MediaArtwork
        {
            MediaItemId = mediaItemId,
            Kind = (ArtworkKind)r.Kind,
            Url = r.Url,
            Language = r.Language,
            Width = r.Width,
            Height = r.Height,
            VoteAverage = r.VoteAverage,
            ProviderId = provider.Id,
            IsUserUploaded = false
        }).ToList();

        await _repository.ReplaceProviderMediaArtworkAsync(mediaItemId, newArtwork);
    }

    public async Task<string> UploadAsync(Guid mediaItemId, UploadedFile file, ArtworkKind kind)
    {
        byte[] imageBytes;
        await using (var buffer = new MemoryStream())
        {
            await file.Content.CopyToAsync(buffer);
            imageBytes = buffer.ToArray();
        }

        var ext = Vora.Application.FileSystem.ImageContentValidator.DetectImageExtension(imageBytes);
        if (ext == null)
        {
            throw new InvalidOperationException("Uploaded file is not a supported image (JPEG, PNG, WebP, or GIF).");
        }

        var fileName = $"media_{mediaItemId}_{kind.ToString().ToLower()}_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(_basePath, fileName);

        try
        {
            await File.WriteAllBytesAsync(filePath, imageBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write uploaded artwork to {FilePath} for media {MediaItemId}.", filePath, mediaItemId);
            throw;
        }

        var publicUrl = $"{CustomArtworkUrlPrefix}{fileName}";
        await _repository.AddMediaArtworkAsync(BuildUserArtwork(mediaItemId, publicUrl, kind, UserUploadProvider));
        return publicUrl;
    }

    public async Task<string> AddUrlAsync(Guid mediaItemId, string url, ArtworkKind kind)
    {
        var fileName = $"media_{mediaItemId}_{kind.ToString().ToLower()}_{Guid.NewGuid()}.jpg";
        var filePath = Path.Combine(_basePath, fileName);

        try
        {
            var imageBytes = await _imageDownloader.DownloadAsync(url);
            await File.WriteAllBytesAsync(filePath, imageBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download artwork from {Url} for media {MediaItemId}.", url, mediaItemId);
            throw;
        }

        var publicUrl = $"{CustomArtworkUrlPrefix}{fileName}";
        await _repository.AddMediaArtworkAsync(BuildUserArtwork(mediaItemId, publicUrl, kind, UserUrlProvider));
        return publicUrl;
    }

    public async Task DeleteAsync(Guid artworkId)
    {
        var artwork = await _repository.GetArtworkByIdAsync(artworkId);
        if (artwork == null || !artwork.IsUserUploaded)
        {
            return;
        }

        if (artwork.Url.StartsWith(CustomArtworkUrlPrefix))
        {
            try
            {
                var fileName = artwork.Url.Split('/').Last();
                var physicalPath = Path.Combine(_basePath, fileName);
                if (File.Exists(physicalPath))
                {
                    File.Delete(physicalPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete physical artwork file for {ArtworkId}.", artworkId);
            }
        }

        await _repository.DeleteMediaArtworkAsync(artworkId);
    }

    private static MediaArtwork BuildUserArtwork(Guid mediaItemId, string publicUrl, ArtworkKind kind, string providerId) => new()
    {
        MediaItemId = mediaItemId,
        Kind = kind,
        Url = publicUrl,
        ProviderId = providerId,
        IsUserUploaded = true,
        Language = "None"
    };
}
