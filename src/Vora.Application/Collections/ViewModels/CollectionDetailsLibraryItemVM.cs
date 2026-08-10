namespace Vora.Application.Collections.ViewModels;

public class CollectionDetailsLibraryItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public DateTime AddedAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? TvShowTitle { get; set; }
    public string? PosterUrl { get; set; }
    public bool IsPlayed { get; set; }
    public int? UnplayedItemCount { get; set; }
}
