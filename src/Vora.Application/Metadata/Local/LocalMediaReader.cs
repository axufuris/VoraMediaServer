using Microsoft.Extensions.Logging;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Metadata.Local;

public sealed record LocalDocument(MetadataResult Document, ExternalIdSet TaggedIds, int? ParsedYear);

public sealed record LocalNaming(string? Title, int? Year, ExternalIdSet TaggedIds);

public interface ILocalMediaReader
{
    Task<LocalDocument?> ReadDocumentAsync(Guid mediaItemId);
    Task<LocalNaming?> ReadNamingAsync(Guid mediaItemId);
    Task<List<MediaArtwork>> FindArtworkAsync(Guid mediaItemId);
}

public sealed class LocalMediaReader(IMediaRepository repository, ILogger<LocalMediaReader> logger) : ILocalMediaReader
{
    public async Task<LocalDocument?> ReadDocumentAsync(Guid mediaItemId)
    {
        var source = await repository.GetLocalMediaSourceAsync(mediaItemId);
        if (source == null) return null;

        return await Task.Run(() =>
        {
            var folders = new LocalMediaFolders(logger);
            var place = LocalMediaPlace.From(source, folders);
            var document = place?.ReadDocument(folders);
            return place == null || document == null
                ? null
                : new LocalDocument(document, place.TaggedIds(folders), place.ParsedYear());
        });
    }

    public async Task<LocalNaming?> ReadNamingAsync(Guid mediaItemId)
    {
        var source = await repository.GetLocalMediaSourceAsync(mediaItemId);
        if (source == null) return null;

        return await Task.Run(() =>
        {
            var folders = new LocalMediaFolders(logger);
            var place = LocalMediaPlace.From(source, folders);
            return place == null ? null : new LocalNaming(place.ParsedTitle(), place.ParsedYear(), place.TaggedIds(folders));
        });
    }

    public async Task<List<MediaArtwork>> FindArtworkAsync(Guid mediaItemId)
    {
        var source = await repository.GetLocalMediaSourceAsync(mediaItemId);
        if (source == null) return [];

        return await Task.Run(() =>
        {
            var folders = new LocalMediaFolders(logger);
            var found = LocalMediaPlace.From(source, folders)?.FindArtwork(folders) ?? [];
            return found.Select(result => new MediaArtwork
            {
                MediaItemId = mediaItemId,
                Url = LocalArtworkAddress.Build(mediaItemId, result.Url),
                Kind = (Vora.Domain.Enums.ArtworkKind)result.Kind,
                Language = result.Language,
                ProviderId = LocalArtworkProvider.ProviderId
            }).ToList();
        });
    }
}
