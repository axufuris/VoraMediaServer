using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Libraries.ViewModels;

public class LibraryItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public DateTime AddedAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? ContentRating { get; set; }
    public string? Resolution { get; set; }
    public int? DurationSeconds { get; set; }
    public int? NumberOfSeasons { get; set; }
    public string? TvShowTitle { get; set; }
    public Guid LibraryId { get; set; }
    public decimal? TimelineOrder { get; set; }
    public bool IsPlayed { get; set; }
    public int? UnplayedItemCount { get; set; }
    public decimal? ServerAdminRating { get; set; }
    public decimal? ThirdPartyRating1 { get; set; }
    public string? ThirdPartyRating1Name { get; set; }
    public decimal? ThirdPartyRating2 { get; set; }
    public string? ThirdPartyRating2Name { get; set; }
    public decimal? MyRating { get; set; }
    public List<string> Genres { get; set; } = new();

    public static Expression<Func<MediaItem, LibraryItemVM>> Projection =>
        item => new LibraryItemVM
        {
            Id = item.Id,
            Title = item.Title,
            SortTitle = item.SortTitle,
            Overview = item.Overview,
            Type = item is Movie ? "Movie"
                : item is TvShow ? "TvShow"
                : item is Season ? "Season"
                : item is Episode ? "Episode"
                : "Unknown",
            PosterUrl = item.PosterUrl,
            BackgroundUrl = item.BackgroundUrl ?? (item is Season ? ((Season)item).TvShow.BackgroundUrl : null),
            ContentRating = item.ContentRating,
            Resolution = item.MediaParts.FirstOrDefault() != null
                ? item.MediaParts.FirstOrDefault()!.Resolution
                : null,
            DurationSeconds = item.MediaParts.FirstOrDefault() != null && item.MediaParts.FirstOrDefault()!.Duration.HasValue
                ? (int?)item.MediaParts.FirstOrDefault()!.Duration!.Value.TotalSeconds
                : null,
            ReleaseDate = item.ReleaseDate,
            AddedAt = item.AddedAt,
            IsPlayed = false,
            UnplayedItemCount = null,
            ServerAdminRating = item.ServerAdminRating,
            ThirdPartyRating1 = item.ThirdPartyRating1,
            ThirdPartyRating1Name = item.ThirdPartyRating1Name,
            ThirdPartyRating2 = item.ThirdPartyRating2,
            ThirdPartyRating2Name = item.ThirdPartyRating2Name,
            NumberOfSeasons = item is TvShow ? ((TvShow)item).Seasons.Count(s => s.MissingSince == null) : (int?)null,
            TvShowTitle = item is Season ? ((Season)item).TvShow.Title : null,
            Genres = item.Genres.Select(g => g.Name).ToList()
        };
}
