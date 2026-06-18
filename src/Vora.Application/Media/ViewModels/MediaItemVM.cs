using System.Linq.Expressions;
using Vora.Application.Actors.ViewModels;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public class MediaItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TmdbId { get; set; }
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public string? ContentRating { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? NumberOfSeasons { get; set; }

    public string? Resolution { get; set; }
    public Guid LibraryId { get; set; }
    public decimal? ServerAdminRating { get; set; }
    public decimal? MyRating { get; set; }
    public List<Guid> CollectionIds { get; set; } = new();

    public List<SeasonVM> Seasons { get; set; } = new();
    public List<CastMemberVM> Cast { get; set; } = new();
    public List<MediaVideoVM> Videos { get; set; } = new();
    public List<string> LockedFields { get; set; } = new();

    public static Expression<Func<MediaItem, MediaItemVM>> Projection =>
        item => new MediaItemVM
        {
            Id = item.Id,
            Title = item.Title,
            SortTitle = item.SortTitle,
            Overview = item.Overview,
            ReleaseDate = item.ReleaseDate,
            Type = item is Movie ? "Movie" : item is TvShow ? "TvShow" : item is Episode ? "Episode" : "Unknown",

            PosterUrl = item.PosterUrl,
            BackgroundUrl = item.BackgroundUrl,
            ContentRating = item.ContentRating,

            Resolution = item.MediaParts.FirstOrDefault() != null ? item.MediaParts.FirstOrDefault()!.Resolution : null,
            LockedFields = item.LockedFields,
            LibraryId = item.LibraryId,
            ServerAdminRating = item.ServerAdminRating,
            CollectionIds = item.Collections.Select(c => c.Id).ToList(),

            Cast = (item is Episode && !item.Cast.Any())
                ? ((Episode)item).Season.TvShow.Cast.OrderBy(c => c.Order).Select(c => new CastMemberVM
                {
                    ActorId = c.ActorId,
                    TmdbId = c.Actor != null ? c.Actor.TmdbId : 0,
                    Name = c.Actor != null ? c.Actor.Name : "Unknown Actor",
                    CharacterName = c.CharacterName,
                    Roles = c.Roles,
                    ProfileImageUrl = c.Actor != null ? c.Actor.ProfileImageUrl : null
                }).ToList()
                : item.Cast.OrderBy(c => c.Order).Select(c => new CastMemberVM
                {
                    ActorId = c.ActorId,
                    TmdbId = c.Actor != null ? c.Actor.TmdbId : 0,
                    Name = c.Actor != null ? c.Actor.Name : "Unknown Actor",
                    CharacterName = c.CharacterName,
                    Roles = c.Roles,
                    ProfileImageUrl = c.Actor != null ? c.Actor.ProfileImageUrl : null
                }).ToList(),

            NumberOfSeasons = item is TvShow ? ((TvShow)item).Seasons.Count : (int?)null,

            Seasons = item is TvShow
                ? ((TvShow)item).Seasons.Select(s => new SeasonVM
                {
                    Id = s.Id,
                    SeasonNumber = s.SeasonNumber,
                    Title = s.Title,
                    PosterUrl = s.PosterUrl,
                    EpisodeCount = s.Episodes.Count
                }).ToList()
                : new List<SeasonVM>(),

            Videos = item.Videos.Select(v => new MediaVideoVM
            {
                VideoKey = v.VideoKey,
                Name = v.Name,
                Site = v.Site,
                Type = v.Type,
                IsOfficial = v.IsOfficial
            }).ToList()
        };
}
