using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public class SeasonDetailsVM
{
    public Guid Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }

    public int? EpisodeCount { get; set; } = 0;

    public bool IsPlayed { get; set; }
    public int? UnplayedItemCount { get; set; }

    public Guid TvShowId { get; set; }
    public string TvShowTitle { get; set; } = string.Empty;
    public string? UpcomingEpisodesJson { get; set; }
    public DateTime? ReleaseDate { get; set; }

    public List<EpisodeVM> Episodes { get; set; } = new();

    public List<string> LockedFields { get; set; } = new();

    public static Expression<Func<MediaItem, SeasonDetailsVM>> Projection =>
        item => new SeasonDetailsVM
        {
            Id = item.Id,
            SeasonNumber = ((Season)item).SeasonNumber,
            Title = item.Title,
            Overview = item.Overview,
            PosterUrl = item.PosterUrl,
            ReleaseDate = item.ReleaseDate,
            EpisodeCount = ((Season)item).Episodes.Count,
            LockedFields = item.LockedFields,

            TvShowId = ((Season)item).TvShowId,
            TvShowTitle = ((Season)item).TvShow.Title,
            UpcomingEpisodesJson = ((Season)item).TvShow.UpcomingEpisodesJson,

            Episodes = ((Season)item).Episodes.Select(e => new EpisodeVM
            {
                Id = e.Id,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                Overview = e.Overview,
                PosterUrl = e.PosterUrl,
                ReleaseDate = e.ReleaseDate,
                DurationMinutes = e.Analysis.Duration.HasValue ? (int)e.Analysis.Duration.Value.TotalMinutes : (int?)null
            }).OrderBy(e => e.EpisodeNumber).ToList()
        };
}
