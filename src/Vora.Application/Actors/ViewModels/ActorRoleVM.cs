using Vora.Domain.Enums;

namespace Vora.Application.Actors.ViewModels;

public class ActorRoleVM
{
    public Guid Id { get; set; }
    public string? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public Guid LibraryId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public MediaCastRole Roles { get; set; }
    public string Role => MediaCastRoleFormatter.Format(Roles);
    public int SortOrder { get; set; }
}
