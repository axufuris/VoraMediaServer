using Vora.Application.Search.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public interface IMusicRepository
{
    Task<List<MusicSearchResultVM>> SearchAsync(string query, MusicAccessFilter access, int limit);

    Task<bool> SetTrackLikedAsync(Guid profileId, Guid trackId, bool liked);
    Task<HashSet<Guid>> GetLikedTrackIdsAsync(Guid profileId, IEnumerable<Guid> trackIds);
    Task<List<Track>> GetLikedTracksAsync(Guid profileId, MusicAccessFilter access);

    Task<Dictionary<Guid, decimal>> GetAlbumRatingsAsync(Guid profileId, IEnumerable<Guid> albumIds);
    Task<Dictionary<Guid, decimal>> GetArtistRatingsAsync(Guid profileId, IEnumerable<Guid> artistIds);
    Task<SetMusicRatingResult> SetAlbumRatingAsync(Guid profileId, Guid albumId, decimal? rating, bool isAdmin);
    Task<SetMusicRatingResult> SetArtistRatingAsync(Guid profileId, Guid artistId, decimal? rating, bool isAdmin);

    Task RecordPlayAsync(Guid profileId, Guid trackId, int durationListenedSeconds, bool completed);
    Task<List<Track>> GetRecentlyPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int limit);
    Task<List<Track>> GetTopPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int limit);
    Task<List<Artist>> GetTopPlayedArtistsAsync(Guid profileId, MusicAccessFilter access, int limit);
    Task<List<Album>> GetRecentlyAddedAlbumsAsync(MusicAccessFilter access, int limit);

    Task<Artist?> GetArtistByNameAsync(Guid libraryId, string name);
    Task<Album?> GetAlbumByTitleAsync(Guid artistId, string title);
    Task<Track?> GetTrackByAlbumAndNumberAsync(Guid albumId, int trackNumber, int? discNumber);
    Task AddArtistAsync(Artist artist);
    Task AddAlbumAsync(Album album);
    Task UpdateArtistAsync(Artist artist);
    Task UpdateAlbumAsync(Album album);

    Task<List<Artist>> GetArtistsAsync(Guid? libraryId, MusicAccessFilter access, int? limit = null);
    Task<List<Album>> GetAlbumsForArtistAsync(Guid artistId, MusicAccessFilter access);
    Task<Artist?> GetArtistByIdAsync(Guid artistId, MusicAccessFilter access);
    Task<Album?> GetAlbumByIdAsync(Guid albumId, MusicAccessFilter access);
    Task<List<Track>> GetTracksForAlbumAsync(Guid albumId, MusicAccessFilter access);
    Task<List<Track>> GetTracksForArtistAsync(Guid artistId, MusicAccessFilter access);
    Task<Track?> GetTrackByIdAsync(Guid trackId, MusicAccessFilter access);
    Task<string?> GetTrackFilePathAsync(Guid trackId, MusicAccessFilter access);

    Task<Artist?> GetArtistForUpdateAsync(Guid artistId);
    Task<Album?> GetAlbumForUpdateAsync(Guid albumId);
    Task<Track?> GetTrackForUpdateAsync(Guid trackId);
    Task UpdateTrackAsync(Track track);

    Task<List<GenreSummary>> GetGenreSummariesAsync(MusicAccessFilter access);
    Task<GenreContent> GetGenreContentAsync(string genre, MusicAccessFilter access);

    Task<(List<AdminPlayHistoryRow> Rows, int Total)> GetAdminPlayHistoryAsync(Guid? profileId, DateTime? from, DateTime? to, string? search, int page, int pageSize);
    Task<List<AdminTopTrackRow>> GetServerTopTracksAsync(DateTime? from, DateTime? to, int limit);
    Task<List<AdminTopArtistRow>> GetServerTopArtistsAsync(DateTime? from, DateTime? to, int limit);
    Task<List<AdminProfilePlayCount>> GetPlaysPerProfileAsync(DateTime? from, DateTime? to);
}

public sealed class AdminPlayHistoryRow
{
    public Guid Id { get; init; }
    public Guid ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public Guid TrackId { get; init; }
    public required string TrackTitle { get; init; }
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public string? AlbumArtworkUrl { get; init; }
    public DateTime PlayedAt { get; init; }
    public int DurationListenedSeconds { get; init; }
    public bool Completed { get; init; }
}

public sealed class AdminTopTrackRow
{
    public Guid TrackId { get; init; }
    public required string TrackTitle { get; init; }
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public string? AlbumArtworkUrl { get; init; }
    public int PlayCount { get; init; }
}

public sealed class AdminTopArtistRow
{
    public Guid ArtistId { get; init; }
    public required string ArtistName { get; init; }
    public string? ArtworkUrl { get; init; }
    public int PlayCount { get; init; }
}

public sealed class AdminProfilePlayCount
{
    public Guid ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public int PlayCount { get; init; }
}

public sealed class GenreSummary
{
    public required string Name { get; init; }
    public int TrackCount { get; init; }
    public int AlbumCount { get; init; }
    public int ArtistCount { get; init; }
    public string? SampleArtworkUrl { get; init; }
}

public sealed class GenreContent
{
    public required string Name { get; init; }
    public List<Artist> Artists { get; set; } = new();
    public List<Album> Albums { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();
}

public sealed class SetMusicRatingResult
{
    public bool Found { get; init; }
    public bool ServerAdminRatingChanged { get; init; }
}

public class MusicAccessFilter
{
    public bool HasAllLibraryAccess { get; init; } = true;
    public List<Guid> AllowedLibraryIds { get; init; } = new();
    public bool HasAllRatings { get; init; } = true;
    public List<string> AllowedRatings { get; init; } = new();
    public bool BlockUnratedContent { get; init; }

    public static MusicAccessFilter Unrestricted => new();
}
