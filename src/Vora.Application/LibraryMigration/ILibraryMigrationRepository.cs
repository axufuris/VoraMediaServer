namespace Vora.Application.LibraryMigration;

public class MediaItemMatchRow
{
    public required Guid Id { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
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
    Task BulkUpsertWatchStatesAsync(Guid profileId, IReadOnlyCollection<WatchStateUpsert> entries);
    Task BulkUpsertRatingsAsync(Guid profileId, IReadOnlyCollection<RatingUpsert> entries);
}
