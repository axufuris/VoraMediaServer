namespace Vora.Domain.Entities.Users;

public class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? PinHash { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsAdmin { get; set; }

    public bool HasAllLibraryAccess { get; set; } = true;
    public List<Guid> AllowedLibraryIds { get; set; } = new();

    public bool HasAllIptvAccess { get; set; } = true;
    public List<Guid> AllowedIptvPlaylistIds { get; set; } = new();

    public bool BlockUnratedContent { get; set; }
    public List<string> AllowedMovieRatings { get; set; } = new();
    public List<string> AllowedTvRatings { get; set; } = new();
    public List<string> AllowedMusicRatings { get; set; } = new();

    public bool AutoApproveRequests { get; set; }
    public bool CanRecordLiveTv { get; set; }
    public bool CanAddCustomPodcastFeeds { get; set; } = true;

    public string? LastFmSessionKey { get; set; }
    public string? LastFmUsername { get; set; }

    public string? ShowtimesLocation { get; set; }

    public string? RadioPrefsJson { get; set; }

    public bool AutoSkipIntro { get; set; }
    public bool AutoSkipCredits { get; set; }
    public int MinimumCreditsSceneSeconds { get; set; } = 15;

    public string? ClientTemplateId { get; set; }
    public string? ScheduleOverrideTemplateId { get; set; }
    public Guid? ScheduleOverrideScheduleId { get; set; }

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;

    public virtual ICollection<ProfileDeviceSetting> DeviceSettings { get; set; } = new List<ProfileDeviceSetting>();
    public virtual ICollection<ProfileAccessSchedule> AccessSchedules { get; set; } = new List<ProfileAccessSchedule>();
}
