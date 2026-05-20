using Vora.Domain.Enums;

namespace Vora.Application.Actors.ViewModels;

public class CastMemberVM
{
    public Guid ActorId { get; set; }
    public int TmdbId { get; set; }
    public required string Name { get; set; }
    public string? CharacterName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public int Order { get; set; }
    public MediaCastRole Roles { get; set; }
    public string Role => MediaCastRoleFormatter.Format(Roles);
}
