namespace Vora.Domain.Entities.Users;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? Nickname { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool HasAllLibraryAccess { get; set; } = true;
    public List<Guid> AllowedLibraryIds { get; set; } = new();

    public bool HasAllIptvAccess { get; set; } = true;
    public List<Guid> AllowedIptvPlaylistIds { get; set; } = new();

    public bool CanRequestMedia { get; set; } = true;
    public bool AutoApproveRequests { get; set; }
    public bool EnableAiRecommendations { get; set; }

    public bool CanRecordLiveTv { get; set; }
    public long DvrStorageQuotaBytes { get; set; }

    public bool CanTimeshiftIptv { get; set; }

    public bool CanAddCustomPodcastFeeds { get; set; }

    public virtual ICollection<UserProfile> Profiles { get; set; } = new List<UserProfile>();
    public virtual ICollection<UserProviderConnection> ProviderConnections { get; set; } = new List<UserProviderConnection>();
}
