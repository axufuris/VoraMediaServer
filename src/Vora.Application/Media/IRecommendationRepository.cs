using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public interface IMusicRecommendationRepository
{
    Task<List<GeneratedMix>> GetMixesForProfileAsync(Guid profileId, GeneratedMixKind kind);
    Task<GeneratedMix?> GetMixByIdAsync(Guid mixId, Guid profileId);
    Task SaveMixAsync(GeneratedMix mix);
    Task DeleteMixesForProfileAsync(Guid profileId, GeneratedMixKind kind);
    Task DeleteMixSlotsAsync(Guid profileId, GeneratedMixKind kind, IReadOnlyCollection<int> slots);

    Task<List<Guid>> GetProfileIdsWithRecentActivityAsync(int withinDays);

    Task<List<ArtistPlayScore>> GetTopArtistsForProfileAsync(Guid profileId, MusicAccessFilter access, int withinDays, int limit);
    Task<List<Track>> GetTopTracksByArtistAsync(Guid artistId, MusicAccessFilter access, Guid? profileId, int limit, int maxPerAlbum);
    Task<Dictionary<Guid, List<string>>> GetGenresForArtistsAsync(IEnumerable<Guid> artistIds);
    Task<List<Track>> GetTracksByIdsAsync(IEnumerable<Guid> trackIds, MusicAccessFilter access);

    Task<List<Track>> GetLikedTracksByArtistsAsync(Guid profileId, IEnumerable<Guid> artistIds, MusicAccessFilter access, int limit);
    Task<List<Track>> GetLikedTracksByGenreAsync(Guid profileId, IEnumerable<string> genres, MusicAccessFilter access, int limit);

    Task<List<Track>> GetRecentTopPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int withinDays, int limit);

    Task<List<Track>> GetTopTracksByGenresAsync(IEnumerable<string> genres, MusicAccessFilter access, Guid? excludeArtistId, IEnumerable<Guid> excludeTrackIds, int limit);
    Task<List<Track>> GetTracksByGenreAsync(string genre, MusicAccessFilter access, IEnumerable<Guid> excludeTrackIds, int limit);
    Task<List<string>> GetAlbumGenresForArtistAsync(Guid artistId);

    Task<List<Station>> GetStationsForProfileAsync(Guid profileId);
    Task<Station?> GetStationByIdAsync(Guid stationId, Guid profileId);
    Task AddStationAsync(Station station);
    Task UpdateStationAsync(Station station);
    Task DeleteStationAsync(Station station);

    Task<List<YearPlayRow>> GetPlaysForYearAsync(Guid profileId, MusicAccessFilter access, int year);
    Task<List<int>> GetYearsWithHistoryAsync(Guid profileId);
    Task<HashSet<Guid>> GetArtistsFirstPlayedInYearAsync(Guid profileId, MusicAccessFilter access, int year);

    Task<List<ArtistSimilarity>> GetSimilaritiesAsync(Guid artistId);
    Task ReplaceSimilaritiesAsync(Guid artistId, IEnumerable<ArtistSimilarity> entries);

    Task<List<ArtistTag>> GetArtistTagsAsync(Guid artistId);
    Task ReplaceArtistTagsAsync(Guid artistId, IEnumerable<ArtistTag> entries);

    Task<Dictionary<string, Domain.Entities.Media.Artist>> GetArtistsByNamesAsync(IEnumerable<string> names, MusicAccessFilter access);

    Task<List<Guid>> GetActiveArtistIdsForProfileAsync(Guid profileId, int withinDays);
    Task<List<Track>> GetRecentlyAddedTracksByArtistsAsync(IEnumerable<Guid> artistIds, MusicAccessFilter access, int withinDays, int limit);
}

public sealed class YearPlayRow
{
    public required Guid TrackId { get; init; }
    public required string TrackTitle { get; init; }
    public string? TrackArtist { get; init; }
    public Guid? AlbumId { get; init; }
    public string? AlbumTitle { get; init; }
    public string? AlbumArtworkUrl { get; init; }
    public string? AlbumGenre { get; init; }
    public Guid? ArtistId { get; init; }
    public string? ArtistName { get; init; }
    public string? ArtistArtworkUrl { get; init; }
    public required DateTime PlayedAt { get; init; }
    public int DurationListenedSeconds { get; init; }
}

public sealed class ArtistPlayScore
{
    public required Guid ArtistId { get; init; }
    public required string ArtistName { get; init; }
    public required double Score { get; init; }
}
