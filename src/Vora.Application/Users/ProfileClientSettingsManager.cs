using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Users;

public interface IProfileClientSettingsManager
{
    Task<ClientSettingsVM> GetAsync(Guid profileId);
    Task<ClientSettingsVM> SaveAsync(Guid profileId, SaveClientSettingsRequest request);
}

public sealed class ProfileClientSettingsManager(IUserRepository users, ISystemSettingsRepository settingsRepository, IClientNotifier notifier) : IProfileClientSettingsManager
{
    public async Task<ClientSettingsVM> GetAsync(Guid profileId) =>
        await ViewOfAsync(await ProfileAsync(profileId));

    public async Task<ClientSettingsVM> SaveAsync(Guid profileId, SaveClientSettingsRequest request)
    {
        var profile = await ProfileAsync(profileId);

        var playback = request.Playback is null ? null : profile.PlaybackOf().Updated(request.Playback);
        var tracks = request.Tracks is null ? null : TrackPreferences.From(request.Tracks);
        var sort = request.LibrarySort is null ? null : LibrarySortChoice.From(request.LibrarySort);

        if (playback is not null) profile.Apply(playback);
        if (tracks is not null) profile.Apply(tracks);
        if (sort is not null) profile.Apply(sort);

        await users.UpdateProfileAsync(profile);
        await notifier.NotifyClientSettingsChangedAsync(profile.Id);
        return await ViewOfAsync(profile);
    }

    private async Task<UserProfile> ProfileAsync(Guid profileId) =>
        await users.GetProfileByIdAsync(profileId) ?? throw new KeyNotFoundException("Profile not found.");

    private async Task<ClientSettingsVM> ViewOfAsync(UserProfile profile) => new()
    {
        Playback = profile.PlaybackOf().ToViewModel(),
        Tracks = profile.TracksOf().ToViewModel(await settingsRepository.GetSettingsAsync()),
        LibrarySort = profile.LibrarySortOf().ToViewModel()
    };
}
