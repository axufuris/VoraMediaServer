namespace Vora.Application.LibraryMigration;

public class MediaItemMatchRow
{
    public required Guid Id { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
}

public class EpisodeMatchRow
{
    public required Guid Id { get; set; }
    public string? ShowTmdbId { get; set; }
    public string? ShowImdbId { get; set; }
    public string? ShowTvdbId { get; set; }
    public required int SeasonNumber { get; set; }
    public required int EpisodeNumber { get; set; }
}

public class WatchStateUpsert
{
    public required Guid MediaItemId { get; set; }
    public required bool IsPlayed { get; set; }
    public required double ResumePositionSeconds { get; set; }
    public DateTime? LastPlayedAt { get; set; }
}

public class RatingUpsert
{
    public required Guid MediaItemId { get; set; }
    public required decimal Rating { get; set; }
    public DateTime? RatedAt { get; set; }
}

public interface ILibraryMigrationRepository
{
    Task<List<MediaItemMatchRow>> FindMatchesAsync(IReadOnlyCollection<string> tmdbIds, IReadOnlyCollection<string> imdbIds, IReadOnlyCollection<string> tvdbIds);
    Task<List<EpisodeMatchRow>> FindEpisodeMatchesAsync(IReadOnlyCollection<string> showTmdbIds, IReadOnlyCollection<string> showImdbIds, IReadOnlyCollection<string> showTvdbIds);
    Task BulkUpsertWatchStatesAsync(Guid profileId, IReadOnlyCollection<WatchStateUpsert> entries);
    Task BulkUpsertRatingsAsync(Guid profileId, IReadOnlyCollection<RatingUpsert> entries);
    Task BulkSetAdminRatingsAsync(IReadOnlyCollection<RatingUpsert> entries);
}
