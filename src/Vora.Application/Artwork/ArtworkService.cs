using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.FileSystem;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Artwork;

public interface IArtworkService
{
    Task<IEnumerable<Vora.Plugins.Dtos.ArtworkResult>> GetArtworkOptionsAsync(Guid mediaItemId);
    Task<string> UploadAsync(Guid mediaItemId, UploadedFile file, ArtworkKind kind);
    Task<string> AddUrlAsync(Guid mediaItemId, string url, ArtworkKind kind);
    Task DeleteAsync(Guid artworkId);
}

public class ArtworkService : IArtworkService
{
    private const string CustomArtworkUrlPrefix = "/api/artwork/custom/";
    private const string UserUploadProvider = "user_upload";
    private const string UserUrlProvider = "user_url";

    private readonly IMediaArtworkRepository _repository;
    private readonly ISafeImageDownloader _imageDownloader;
    private readonly ILogger<ArtworkService> _logger;
    private readonly string _basePath;

    public ArtworkService(
        IMediaArtworkRepository repository,
        IOptions<StoragePathsOptions> storagePaths,
        ISafeImageDownloader imageDownloader,
        ILogger<ArtworkService> logger)
    {
        _repository = repository;
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

    public async Task<IEnumerable<Vora.Plugins.Dtos.ArtworkResult>> GetArtworkOptionsAsync(Guid mediaItemId)
    {
        var artwork = await _repository.GetMediaArtworkAsync(mediaItemId);
        return artwork.Select(a => new Vora.Plugins.Dtos.ArtworkResult
        {
            Id = a.Id.ToString(),
            IsUserUploaded = a.IsUserUploaded,
            Url = a.Url,
            Kind = (Vora.Plugins.Dtos.ArtworkKind)a.Kind,
            Language = a.Language,
            Width = a.Width,
            Height = a.Height,
            VoteAverage = a.VoteAverage
        });
    }

    public async Task<string> UploadAsync(Guid mediaItemId, UploadedFile file, ArtworkKind kind)
    {
        var ext = Path.GetExtension(file.FileName);
        var fileName = $"media_{mediaItemId}_{kind.ToString().ToLower()}_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(_basePath, fileName);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.Content.CopyToAsync(stream);
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
