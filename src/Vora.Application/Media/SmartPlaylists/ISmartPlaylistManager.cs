using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Media.SmartPlaylists;

public interface ISmartPlaylistManager
{
    Task<List<SmartPlaylistSummaryVM>> ListAsync(Guid profileId, MusicAccessFilter access);
    Task<SmartPlaylistDetailVM?> GetAsync(Guid id, Guid profileId, MusicAccessFilter access);
    Task<SmartPlaylistSummaryVM> CreateAsync(Guid profileId, SmartPlaylistSaveRequest request);
    Task<SmartPlaylistSummaryVM?> UpdateAsync(Guid id, Guid profileId, SmartPlaylistSaveRequest request);
    Task DeleteAsync(Guid id, Guid profileId);
    Task<int> PreviewCountAsync(Guid profileId, MusicAccessFilter access, PlaylistMediaType mediaType, SmartPlaylistDefinition definition);
    Task<SmartPlaylistItemsVM> GetItemsAsync(Guid id, Guid profileId, MusicAccessFilter access);
}

public sealed class SmartPlaylistSaveRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }
    public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Music;
    public SmartPlaylistDefinition Definition { get; set; } = new();
}

public sealed class SmartPlaylistSummaryVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }
    public PlaylistMediaType MediaType { get; set; }
    public int TrackCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SmartPlaylistDetailVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }
    public PlaylistMediaType MediaType { get; set; }
    public SmartPlaylistDefinition Definition { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SmartPlaylistItemsVM
{
    public PlaylistMediaType MediaType { get; set; }
    public List<Vora.Application.Media.ViewModels.ArtistTrackVM>? Tracks { get; set; }
    public List<SmartPlaylistMovieVM>? Movies { get; set; }
    public List<SmartPlaylistEpisodeVM>? Episodes { get; set; }
}

public sealed class SmartPlaylistMovieVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ContentRating { get; set; }
    public bool IsWatched { get; set; }
}

public sealed class SmartPlaylistEpisodeVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? PosterUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ContentRating { get; set; }
    public bool IsWatched { get; set; }
}
