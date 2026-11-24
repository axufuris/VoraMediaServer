using Vora.Application.Analysis;
using Vora.Application.Streaming;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Settings;

public interface IClientDefaultsManager
{
    Task<ClientDefaultsVM> GetAsync();
    Task<ClientDefaultsVM> SaveAsync(SaveClientDefaultsRequest request);
    Task ApplyToNewProfileAsync(UserProfile profile);
    Task<DeviceSettings> GetDeviceDefaultsAsync();
}

public sealed class ClientDefaultsManager(ISystemSettingsRepository settingsRepository, IClientNotifier notifier) : IClientDefaultsManager
{
    public async Task<ClientDefaultsVM> GetAsync() =>
        ViewOf(await settingsRepository.GetSettingsAsync());

    public async Task<ClientDefaultsVM> SaveAsync(SaveClientDefaultsRequest request)
    {
        var settings = await settingsRepository.GetSettingsForUpdateAsync();

        var playback = request.Playback is null ? null : PlaybackOf(settings).Updated(request.Playback);
        var tracks = request.Tracks is null ? null : TrackPreferences.From(request.Tracks);
        var sort = request.LibrarySort is null ? null : LibrarySortChoice.From(request.LibrarySort);
        var device = request.Device is null ? null : DeviceSettings.From(request.Device);

        if (playback is not null) Store(settings, playback);
        if (tracks is not null) Store(settings, tracks);
        if (sort is not null) Store(settings, sort);
        if (device is not null) Store(settings, device);

        await settingsRepository.SaveChangesAsync();
        if (device is not null) await notifier.NotifyClientDefaultsChangedAsync();
        return ViewOf(settings);
    }

    public async Task<DeviceSettings> GetDeviceDefaultsAsync() =>
        DeviceOf(await settingsRepository.GetSettingsAsync());

    public async Task ApplyToNewProfileAsync(UserProfile profile)
    {
        var settings = await settingsRepository.GetSettingsAsync();
        profile.Apply(PlaybackOf(settings));
        profile.Apply(TracksOf(settings));
        profile.Apply(LibrarySortOf(settings));
    }

    private static ClientDefaultsVM ViewOf(ServerSetting settings) => new()
    {
        Playback = PlaybackOf(settings).ToViewModel(),
        Tracks = TracksOf(settings).ToViewModel(settings),
        LibrarySort = LibrarySortOf(settings).ToViewModel(),
        Device = DeviceOf(settings).ToViewModel()
    };

    private static DeviceSettings DeviceOf(ServerSetting settings) => new(
        settings.DeviceDefaultBitrate,
        settings.DeviceDefaultMaxResolution,
        settings.DeviceDefaultSeekStepSeconds,
        settings.DeviceDefaultVolumeStepPercent,
        settings.DeviceDefaultSoundOnClick,
        settings.DeviceDefaultSoundOnNavOpen,
        settings.DeviceDefaultSoundOnMove);

    private static void Store(ServerSetting settings, DeviceSettings device)
    {
        settings.DeviceDefaultBitrate = device.Bitrate;
        settings.DeviceDefaultMaxResolution = device.MaxResolution;
        settings.DeviceDefaultSeekStepSeconds = device.SeekStepSeconds;
        settings.DeviceDefaultVolumeStepPercent = device.VolumeStepPercent;
        settings.DeviceDefaultSoundOnClick = device.SoundOnClick;
        settings.DeviceDefaultSoundOnNavOpen = device.SoundOnNavOpen;
        settings.DeviceDefaultSoundOnMove = device.SoundOnMove;
    }

    private static PlaybackBehaviour PlaybackOf(ServerSetting settings) => new(
        settings.NewProfileAutoSkipIntro,
        settings.NewProfileAutoSkipCredits,
        settings.NewProfileAutoPlayNext,
        settings.NewProfileUpNextOrder,
        settings.NewProfileEpisodeLayout,
        settings.NewProfileMinimumCreditsSceneSeconds);

    private static void Store(ServerSetting settings, PlaybackBehaviour playback)
    {
        settings.NewProfileAutoSkipIntro = playback.AutoSkipIntro;
        settings.NewProfileAutoSkipCredits = playback.AutoSkipCredits;
        settings.NewProfileAutoPlayNext = playback.AutoPlayNext;
        settings.NewProfileUpNextOrder = playback.UpNextOrder;
        settings.NewProfileEpisodeLayout = playback.EpisodeLayout;
        settings.NewProfileMinimumCreditsSceneSeconds = playback.MinimumCreditsSceneSeconds;
    }

    private static TrackPreferences TracksOf(ServerSetting settings) => new(
        settings.NewProfileAudioLanguage,
        settings.NewProfileSubtitleMode,
        settings.NewProfileSubtitleLanguage,
        settings.NewProfilePreferHearingImpairedSubtitles);

    private static void Store(ServerSetting settings, TrackPreferences tracks)
    {
        settings.NewProfileAudioLanguage = tracks.AudioLanguage;
        settings.NewProfileSubtitleLanguage = tracks.SubtitleLanguage;
        settings.NewProfileSubtitleMode = tracks.SubtitleMode;
        settings.NewProfilePreferHearingImpairedSubtitles = tracks.PreferHearingImpaired;
    }

    private static LibrarySortChoice LibrarySortOf(ServerSetting settings) => new(
        settings.NewProfileLibrarySort,
        settings.NewProfileLibrarySortDescending);

    private static void Store(ServerSetting settings, LibrarySortChoice sort)
    {
        settings.NewProfileLibrarySort = sort.Sort;
        settings.NewProfileLibrarySortDescending = sort.Descending;
    }
}
