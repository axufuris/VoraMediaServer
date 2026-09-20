using System.Text.Json.Serialization;

namespace Vora.Domain.Entities.Playlists;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlaylistMediaType
{
    Mixed = 0,
    Music = 1,
    Movies = 2,
    Shows = 3
}
