using System.Linq.Expressions;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Users.ViewModels;

public class UserProfileVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string? ProfileImageUrl { get; set; }
    public bool HasPin { get; set; }
    public bool BlockUnratedContent { get; set; }
    public bool HasAllLibraryAccess { get; set; }
    public List<Guid> AllowedLibraryIds { get; set; } = new();
    public bool HasAllIptvAccess { get; set; }
    public List<Guid> AllowedIptvPlaylistIds { get; set; } = new();
    public List<string> AllowedMovieRatings { get; set; } = new();
    public List<string> AllowedTvRatings { get; set; } = new();
    public List<string> AllowedMusicRatings { get; set; } = new();
    public List<ProfileScheduleVM> AccessSchedules { get; set; } = new();
    public bool CanRecordLiveTv { get; set; }
    public bool CanAddCustomPodcastFeeds { get; set; }
    public string? LastFmUsername { get; set; }

    public static Expression<Func<UserProfile, UserProfileVM>> Projection =>
        p => new UserProfileVM
        {
            Id = p.Id,
            Name = p.Name,
            IsAdmin = p.IsAdmin,
            ProfileImageUrl = p.ProfileImageUrl,
            HasPin = p.PinHash != null && p.PinHash != "",
            BlockUnratedContent = p.BlockUnratedContent,
            HasAllLibraryAccess = p.HasAllLibraryAccess,
            AllowedLibraryIds = p.AllowedLibraryIds,
            HasAllIptvAccess = p.HasAllIptvAccess,
            AllowedIptvPlaylistIds = p.AllowedIptvPlaylistIds,
            AllowedMovieRatings = p.AllowedMovieRatings,
            AllowedTvRatings = p.AllowedTvRatings,
            AllowedMusicRatings = p.AllowedMusicRatings,
            CanRecordLiveTv = p.CanRecordLiveTv,
            CanAddCustomPodcastFeeds = p.CanAddCustomPodcastFeeds,
            LastFmUsername = p.LastFmUsername,
            AccessSchedules = p.AccessSchedules.Select(s => new ProfileScheduleVM
            {
                DayOfWeek = (int)s.DayOfWeek,
                StartTime = s.StartTime.ToString(@"hh\:mm"),
                EndTime = s.EndTime.ToString(@"hh\:mm")
            }).ToList()
        };
}