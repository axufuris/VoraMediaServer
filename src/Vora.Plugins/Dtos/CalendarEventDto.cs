namespace Vora.Plugins.Dtos;

public class CalendarEventDto
{
    public string Id { get; set; } = string.Empty;
    public Guid? LibraryId { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalProviderId { get; set; }
    public Guid? LibraryItemId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? SubTitle { get; set; }
    public string MediaType { get; set; } = string.Empty;

    public DateTime ReleaseDate { get; set; }
    public TimeSpan? AirTime { get; set; }
    public string ReleaseType { get; set; } = "Release";

    public string ContentRating { get; set; } = "Unrated";

    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }

    public bool IsInLibrary { get; set; }
    public bool IsWatchlisted { get; set; }
}
