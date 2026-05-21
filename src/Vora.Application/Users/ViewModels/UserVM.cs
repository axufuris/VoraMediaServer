using System.Linq.Expressions;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Users.ViewModels;

public class UserVM
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool HasAllLibraryAccess { get; set; }
    public List<Guid> AllowedLibraryIds { get; set; } = new();
    public bool CanRequestMedia { get; set; }
    public bool AutoApproveRequests { get; set; }
    public bool EnableAiRecommendations { get; set; }
    public bool EmailNotifyOnRequestAvailable { get; set; }
    public bool HasAllIptvAccess { get; set; }
    public List<Guid> AllowedIptvPlaylistIds { get; set; } = new();
    public bool CanRecordLiveTv { get; set; }
    public long DvrStorageQuotaBytes { get; set; }
    public bool CanTimeshiftIptv { get; set; }
    public bool CanAddCustomPodcastFeeds { get; set; }

    public IEnumerable<UserProfileVM> Profiles { get; set; } = new List<UserProfileVM>();

    public static Expression<Func<User, UserVM>> Projection =>
        u => new UserVM
        {
            Id = u.Id,
            Email = u.Email,
            DisplayName = u.DisplayName,
            IsAdmin = u.IsAdmin,
            HasAllLibraryAccess = u.HasAllLibraryAccess,
            AllowedLibraryIds = u.AllowedLibraryIds,
            CanRequestMedia = u.CanRequestMedia,
            AutoApproveRequests = u.AutoApproveRequests,
            EnableAiRecommendations = u.EnableAiRecommendations,
            EmailNotifyOnRequestAvailable = u.EmailNotifyOnRequestAvailable,
            HasAllIptvAccess = u.HasAllIptvAccess,
            AllowedIptvPlaylistIds = u.AllowedIptvPlaylistIds,
            CanRecordLiveTv = u.CanRecordLiveTv,
            DvrStorageQuotaBytes = u.DvrStorageQuotaBytes,
            CanTimeshiftIptv = u.CanTimeshiftIptv,
            CanAddCustomPodcastFeeds = u.CanAddCustomPodcastFeeds,
            Profiles = u.Profiles.AsQueryable().Select(UserProfileVM.Projection).ToList()
        };
}