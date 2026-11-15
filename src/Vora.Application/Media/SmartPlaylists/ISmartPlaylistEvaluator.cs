using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Media.SmartPlaylists;

public interface ISmartPlaylistEvaluator
{
    Task<List<MediaItem>> EvaluateAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, PlaylistAccessFilter access);

    // A smart COLLECTION's members: whole titles, never episodes or tracks.
    // Movies → films, Shows → shows (the TvShow itself), Mixed → both.
    // Collections are shared, so nothing here is per-profile.
    Task<List<Guid>> EvaluateCollectionIdsAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, PlaylistAccessFilter access);
    Task<int> CountAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, PlaylistAccessFilter access);
}
