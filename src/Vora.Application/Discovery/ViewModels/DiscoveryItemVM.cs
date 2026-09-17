using Vora.Domain.Enums;

namespace Vora.Application.Discovery.ViewModels;

public class DiscoveryItemVM
{
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = "Movie";
    public int? Year { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? ContentRating { get; set; }
    public bool InLibrary { get; set; }

    // The local item this title resolves to, when it is in the library. Lets a
    // client open the copy on the server instead of the provider page.
    public Guid? MediaItemId { get; set; }
    public RequestStatus? RequestStatus { get; set; }
}
