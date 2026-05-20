using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Playlists;

public class SmartPlaylist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }

    public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Music;

    public string RulesJson { get; set; } = "{}";

    public int? Limit { get; set; }
    public string SortBy { get; set; } = "Random";
    public string SortDirection { get; set; } = "Asc";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
