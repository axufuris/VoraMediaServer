using Microsoft.Extensions.Logging;
using Vora.Application.Artwork;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Providers.Local;
using ArtworkKind = Vora.Plugins.Dtos.ArtworkKind;

namespace Vora.Application.Metadata.Local;

public sealed record LocalMetadataChanges(IReadOnlyList<LocalMediaSourceDto> Documents, IReadOnlyList<Guid> Artwork)
{
    public static LocalMetadataChanges None { get; } = new([], []);

    public bool IsEmpty => Documents.Count == 0 && Artwork.Count == 0;
}

public interface ILocalMetadataChangeFinder
{
    Task<LocalMetadataChanges> FindAsync(Guid libraryId, DateTime? since = null, CancellationToken cancellationToken = default);
}

public sealed class LocalMetadataChangeFinder(
    ILibraryRepository libraries,
    IMediaRepository media,
    ILogger<LocalMetadataChangeFinder> logger,
    TimeProvider? time = null) : ILocalMetadataChangeFinder
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<LocalMetadataChanges> FindAsync(Guid libraryId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var settings = await libraries.GetProjectedByIdAsync(libraryId,
            l => new LocalLibrarySettings(l.LocalMetadataMode, l.PreferLocalArtwork, l.MetadataProviderId, l.ArtworkProviderId));
        if (settings == null) return LocalMetadataChanges.None;

        var mode = settings.DocumentMode;
        if (mode == LocalMetadataMode.Off && !settings.ReadsArtwork && since == null) return LocalMetadataChanges.None;

        var sources = await media.GetLibraryLocalMediaSourcesAsync(libraryId);
        var now = _time.GetUtcNow().UtcDateTime;

        return await Task.Run(() =>
        {
            var folders = new LocalMediaFolders(logger);
            var documents = new List<LocalMediaSourceDto>();
            var artwork = new List<Guid>();

            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var place = LocalMediaPlace.From(source, folders);
                if (place == null) continue;

                if (DocumentChanged(place, mode, since, now, folders)) documents.Add(source);
                if (ArtworkChanged(place, settings.ReadsArtwork, folders)) artwork.Add(source.Id);
            }

            return new LocalMetadataChanges(documents, artwork);
        }, cancellationToken);
    }

    private static bool DocumentChanged(LocalMediaPlace place, LocalMetadataMode mode, DateTime? since, DateTime now, LocalMediaFolders folders)
    {
        var source = place.Source;
        if (mode == LocalMetadataMode.Off) return false;
        if (mode == LocalMetadataMode.MatchOnly && source.Kind is not (LocalMediaSourceDto.Movie or LocalMediaSourceDto.TvShow)) return false;
        if (source.LastMetadataRefresh is not DateTime refreshed) return false;

        var documents = place.Documents(folders);
        if (documents.Count == 0) return false;
        if (since is DateTime asked) return refreshed < asked;

        return documents
            .Select(File.GetLastWriteTimeUtc)
            .Any(written => written > refreshed && written <= now);
    }

    private static bool ArtworkChanged(LocalMediaPlace place, bool readsArtwork, LocalMediaFolders folders)
    {
        var source = place.Source;
        var found = readsArtwork ? place.FindArtwork(folders) : [];

        foreach (var (kind, lockedBy, current) in Slots(source))
        {
            if (source.IsLocked(lockedBy)) continue;

            var image = found.FirstOrDefault(result => result.Kind == kind)?.Url;
            if (image == null)
            {
                if (LocalArtworkAddress.Is(current)) return true;
                continue;
            }

            if (LocalArtworkAddress.Build(source.Id, image) != current) return true;
        }

        return false;
    }

    private static IEnumerable<(ArtworkKind Kind, string LockedBy, string? Current)> Slots(LocalMediaSourceDto source)
    {
        if (source.Kind == LocalMediaSourceDto.Episode)
        {
            yield return (ArtworkKind.Backdrop, nameof(MediaItem.BackgroundUrl), source.PosterUrl);
            yield break;
        }

        yield return (ArtworkKind.Poster, nameof(MediaItem.PosterUrl), source.PosterUrl);
        yield return (ArtworkKind.Backdrop, nameof(MediaItem.BackgroundUrl), source.BackgroundUrl);

        if (source.Kind != LocalMediaSourceDto.Season)
        {
            yield return (ArtworkKind.Logo, nameof(MediaItem.LogoUrl), source.LogoUrl);
        }
    }
}
