using Vora.Application.Streaming;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Users;

public static class ProfileClientSettings
{
    public static PlaybackBehaviour PlaybackOf(this UserProfile profile) => new(
        profile.AutoSkipIntro,
        profile.AutoSkipCredits,
        profile.AutoPlayNext,
        profile.UpNextOrder,
        profile.EpisodeLayout,
        profile.MinimumCreditsSceneSeconds);

    public static void Apply(this UserProfile profile, PlaybackBehaviour playback)
    {
        profile.AutoSkipIntro = playback.AutoSkipIntro;
        profile.AutoSkipCredits = playback.AutoSkipCredits;
        profile.AutoPlayNext = playback.AutoPlayNext;
        profile.UpNextOrder = playback.UpNextOrder;
        profile.EpisodeLayout = playback.EpisodeLayout;
        profile.MinimumCreditsSceneSeconds = playback.MinimumCreditsSceneSeconds;
    }

    public static TrackPreferences TracksOf(this UserProfile profile) => new(
        profile.PreferredAudioLanguage,
        profile.SubtitleMode,
        profile.PreferredSubtitleLanguage,
        profile.PreferHearingImpairedSubtitles);

    public static void Apply(this UserProfile profile, TrackPreferences tracks)
    {
        profile.PreferredAudioLanguage = tracks.AudioLanguage;
        profile.PreferredSubtitleLanguage = tracks.SubtitleLanguage;
        profile.SubtitleMode = tracks.SubtitleMode;
        profile.PreferHearingImpairedSubtitles = tracks.PreferHearingImpaired;
    }

    public static LibrarySortChoice LibrarySortOf(this UserProfile profile) => new(
        profile.LibrarySort,
        profile.LibrarySortDescending);

    public static void Apply(this UserProfile profile, LibrarySortChoice sort)
    {
        profile.LibrarySort = sort.Sort;
        profile.LibrarySortDescending = sort.Descending;
    }
}
