namespace Vora.Application.Discovery.ViewModels;

public class DiscoveryActorVM
{
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? Birthday { get; set; }
    public string? Deathday { get; set; }
    public string? ProfileImageUrl { get; set; }
    public List<DiscoveryItemVM> Filmography { get; set; } = new();
}
