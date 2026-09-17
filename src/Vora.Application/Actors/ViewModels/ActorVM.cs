using System.Linq.Expressions;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Actors.ViewModels;

public class ActorVM
{
    public Guid Id { get; set; }
    public int TmdbId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? Biography { get; set; }
    public DateTime? Birthday { get; set; }
    public DateTime? Deathday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<ActorRoleVM> Filmography { get; set; } = new();

    public static Expression<Func<Actor, ActorVM>> Projection =>
        a => new ActorVM
        {
            Id = a.Id,
            TmdbId = a.TmdbId,
            Name = a.Name,
            ProfileImageUrl = a.ProfileImageUrl,
            Biography = a.Biography,
            Birthday = a.Birthday,
            Deathday = a.Deathday,
            PlaceOfBirth = a.PlaceOfBirth,
            Filmography = a.Roles
                // Exclude trashed titles (MissingSince != null): a removed item
                // lingers in Trash but must not show in the actor's filmography.
                .Where(r => r.MediaItem.MissingSince == null)
                .Select(r => new ActorRoleVM
            {
                Id = r.MediaItem.Id,
                TmdbId = r.MediaItem.TmdbId,
                Title = r.MediaItem.Title,
                SortTitle = r.MediaItem.SortTitle,
                ReleaseDate = r.MediaItem.ReleaseDate,
                Type = r.MediaItem is Movie ? "Movie"
                    : r.MediaItem is TvShow ? "TvShow"
                    : r.MediaItem is Episode ? "Episode"
                    : r.MediaItem is Season ? "Season"
                    : "Unknown",
                PosterUrl = r.MediaItem.PosterUrl,
                LibraryId = r.MediaItem.LibraryId,
                Roles = r.Roles,
                SortOrder = r.Order,
                CharacterName = r.CharacterName ?? string.Empty
            })
            .OrderByDescending(r => r.ReleaseDate)
            .ToList()
        };
}
