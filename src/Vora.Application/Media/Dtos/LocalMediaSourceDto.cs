namespace Vora.Application.Media.Dtos;

public sealed record LocalMediaSourceDto(Guid Id, string Kind, int? SeasonNumber, int? EpisodeNumber, IReadOnlyList<string> FilePaths)
{
    public const string Movie = "Movie";
    public const string TvShow = "TvShow";
    public const string Season = "Season";
    public const string Episode = "Episode";

    public Guid? TvShowId { get; init; }
    public DateTime? LastMetadataRefresh { get; init; }
    public string? PosterUrl { get; init; }
    public string? BackgroundUrl { get; init; }
    public string? LogoUrl { get; init; }
    public IReadOnlyList<string> LockedFields { get; init; } = [];

    public bool IsLocked(string field) => LockedFields.Contains(field, StringComparer.OrdinalIgnoreCase);
}
