using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Media.SmartPlaylists;

public interface ISmartPlaylistEvaluator
{
    Task<List<MediaItem>> EvaluateAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, PlaylistAccessFilter access);
    Task<List<Guid>> EvaluateIdsAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, PlaylistAccessFilter access);
    Task<int> CountAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, PlaylistAccessFilter access);
}
