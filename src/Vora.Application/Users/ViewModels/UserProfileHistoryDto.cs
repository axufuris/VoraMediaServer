namespace Vora.Application.Users.ViewModels;

public class UserProfileHistoryDto
{
    public Guid SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TvShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? ReleaseYear { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? ContentRating { get; set; }
    public int DurationMinutes { get; set; }
    public int PausedMinutes { get; set; }
    public string TimeStarted { get; set; } = string.Empty;
    public string? TimeStopped { get; set; }
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public bool IsGrouped { get; set; }
    public List<UserProfileHistoryDto>? SubSessions { get; set; }
}
