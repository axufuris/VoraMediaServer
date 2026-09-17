namespace Vora.Application.Collections.ViewModels;

public class CollectionDetailsLibraryItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public DateTime AddedAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? NumberOfSeasons { get; set; }
    public string? TvShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public string? SeasonName { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? Edition { get; set; }
    public string? PosterUrl { get; set; }
    public bool IsPlayed { get; set; }
    public int? UnplayedItemCount { get; set; }
    public double? InUniverseYear { get; set; }
    public bool InUniverseYearLocked { get; set; }
}
