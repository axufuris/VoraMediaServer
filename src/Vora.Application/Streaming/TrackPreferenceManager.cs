using Vora.Application.Settings;
using Vora.Application.Streaming.Requests;
using Vora.Application.Streaming.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

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

        profile.Apply(TrackPreferences.From(request));
        await _users.UpdateProfileAsync(profile);

        return await ViewOfAsync(profile);
    }

    private async Task<TrackPreferencesVM> ViewOfAsync(UserProfile profile) =>
        profile.TracksOf().ToViewModel(await _settings.GetSettingsAsync());
}
