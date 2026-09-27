using System.Text.Json.Serialization;
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

    // `Roles` is a [Flags] enum which JsonStringEnumConverter serializes as a
    // comma-separated string (e.g. "Actor, Producer") for combined values.
    // OpenAPI schemas describe enums as single-value, so any strictly-typed
    // generated client (Android via kotlinx-serialization) crashes parsing
    // combined values. The web client only uses `Role` (the formatted display
    // string) and doesn't read `Roles` anywhere, so hiding the underlying
    // enum from the wire shape is the cleanest fix. `Role` continues to be
    // computed from `Roles` server-side and is still emitted.
    [JsonIgnore]
    public MediaCastRole Roles { get; set; }
    public string Role => MediaCastRoleFormatter.Format(Roles);
}
