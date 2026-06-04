namespace Vora.Application.Discovery.ViewModels;

public class WatchlistItemVM
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime AddedAt { get; set; }
}
