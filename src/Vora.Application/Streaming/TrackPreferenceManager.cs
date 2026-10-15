using Vora.Application.Settings;
using Vora.Application.Streaming.Requests;
using Vora.Application.Streaming.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Plugins;

namespace Vora.Application.Streaming;

public interface ITrackPreferenceManager
{
    Task<TrackPreferencesVM> GetAsync(Guid profileId);
    Task<TrackPreferencesVM> SaveAsync(Guid profileId, SaveTrackPreferencesRequest request);
}

public sealed class TrackPreferenceManager : ITrackPreferenceManager
{
    private readonly IUserRepository _users;
    private readonly ISystemSettingsRepository _settings;

    public TrackPreferenceManager(IUserRepository users, ISystemSettingsRepository settings)
    {
        _users = users;
        _settings = settings;
    }

    public async Task<TrackPreferencesVM> GetAsync(Guid profileId)
    {
        var profile = await _users.GetProfileByIdAsync(profileId)
            ?? throw new KeyNotFoundException("Profile not found.");
        return await ViewOfAsync(profile);
    }

    public async Task<TrackPreferencesVM> SaveAsync(Guid profileId, SaveTrackPreferencesRequest request)
    {
        var profile = await _users.GetProfileByIdAsync(profileId)
            ?? throw new KeyNotFoundException("Profile not found.");

        profile.PreferredAudioLanguage = LanguageCode(request.AudioLanguage);
        profile.PreferredSubtitleLanguage = LanguageCode(request.SubtitleLanguage);
        profile.SubtitleMode = request.SubtitleMode;
        profile.PreferHearingImpairedSubtitles = request.PreferHearingImpairedSubtitles;
        await _users.UpdateProfileAsync(profile);

        return await ViewOfAsync(profile);
    }

    private async Task<TrackPreferencesVM> ViewOfAsync(UserProfile profile)
    {
        var settings = await _settings.GetSettingsAsync();
        return new TrackPreferencesVM
        {
            AudioLanguage = profile.PreferredAudioLanguage,
            SubtitleLanguage = profile.PreferredSubtitleLanguage,
            SubtitleMode = profile.SubtitleMode,
            PreferHearingImpairedSubtitles = profile.PreferHearingImpairedSubtitles,
            ServerLanguage = settings.MetadataLanguage,
            ServerSubtitleMode = TrackPreferences.ServerSubtitleMode(settings)
        };
    }

    private static string? LanguageCode(string? language)
    {
        var code = MetadataLanguageCodes.Normalize(language);
        if (code is { Length: > 16 }) throw new ArgumentException("That is not a language code.", nameof(language));
        return code;
    }
}
