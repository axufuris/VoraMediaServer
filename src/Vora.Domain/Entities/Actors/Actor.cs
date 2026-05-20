namespace Vora.Domain.Entities.Actors;

public class Actor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Biography { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? HomePage { get; set; }

    public DateTime? Birthday { get; set; }
    public DateTime? Deathday { get; set; }

    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }

    public bool IsCustom { get; set; }

    public virtual ICollection<MediaCastMember> Roles { get; set; } = new List<MediaCastMember>();
}
