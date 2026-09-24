using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence.Extensions;

namespace Vora.Infrastructure.Persistence.Repositories;

// Which of a manual playlist's items a profile may see. A manual playlist holds
// fixed items that its OWNER chose, so once playlists can be shared the viewer's
// parental controls have to be applied to them — a child opening an adult's
// shared playlist must see what their own library would show them and nothing
// more. That covers the items, the count and the poster mosaic alike; a mosaic
// built from every item would put a restricted film's poster in the header even
// with the film itself hidden.
//
// Built from the two rules everything else uses — video browsing's for films and
// episodes, MusicAccessQuery's for tracks — rather than a third.
internal static class PlaylistVisibility
{
    public static IQueryable<Guid> VisibleMediaIds(VoraDbContext context, PlaylistAccessFilter access)
    {
        var video = context.MediaItems
            .ApplyAccessFilters(
                access.HasAllLibraryAccess,
                access.AllowedLibraryIds,
                access.AllowedMovieRatings,
                access.AllowedTvRatings,
                access.BlockUnratedContent)
            .Where(m => !(m is Track))
            .Select(m => m.Id);

        var tracks = context.Tracks
            .ApplyMusicAccess(access.Music)
            .Select(t => t.Id);

        return video.Concat(tracks);
    }
}
