using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Streaming.Requests;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Users;

public class ProfileClientSettingsManagerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly UserProfile _profile = new() { Name = "Sam", AutoSkipIntro = true, PreferredAudioLanguage = "fra" };
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly ProfileClientSettingsManager _manager;

    public ProfileClientSettingsManagerTests()
    {
        _users.GetProfileByIdAsync(_profile.Id).Returns(_profile);
        _settings.GetSettingsAsync().Returns(new ServerSetting { MetadataLanguage = "ita" });
        _manager = new ProfileClientSettingsManager(_users, _settings, _notifier);
    }

    [Fact]
    public async Task Reads_the_profiles_own_values_with_the_server_language()
    {
        var settings = await _manager.GetAsync(_profile.Id);

        settings.Playback.AutoSkipIntro.Should().BeTrue();
        settings.Tracks.AudioLanguage.Should().Be("fra");
        settings.Tracks.ServerLanguage.Should().Be("ita");
        settings.LibrarySort.Sort.Should().BeNull();
    }

    [Fact]
    public async Task Saves_the_sections_sent_in_one_write_and_leaves_the_rest()
    {
        var saved = await _manager.SaveAsync(_profile.Id, new SaveClientSettingsRequest
        {
            Playback = new PlaybackPreferencesVM { AutoSkipCredits = true, MinimumCreditsSceneSeconds = 25 },
            LibrarySort = new LibrarySortVM { Sort = LibrarySortField.ReleaseDate, Descending = true }
        });

        _profile.AutoSkipIntro.Should().BeFalse();
        _profile.AutoSkipCredits.Should().BeTrue();
        _profile.MinimumCreditsSceneSeconds.Should().Be(25);
        _profile.LibrarySort.Should().Be(LibrarySortField.ReleaseDate);
        _profile.PreferredAudioLanguage.Should().Be("fra");
        saved.LibrarySort.Descending.Should().BeTrue();
        saved.Tracks.AudioLanguage.Should().Be("fra");
        await _users.Received(1).UpdateProfileAsync(_profile);
        await _notifier.Received(1).NotifyClientSettingsChangedAsync(_profile.Id);
    }

    [Fact]
    public async Task A_section_with_automatic_picks_clears_the_profiles_track_choices()
    {
        await _manager.SaveAsync(_profile.Id, new SaveClientSettingsRequest { Tracks = new SaveTrackPreferencesRequest() });

        _profile.PreferredAudioLanguage.Should().BeNull();
        _profile.SubtitleMode.Should().BeNull();
    }

    [Fact]
    public async Task A_bad_section_writes_nothing()
    {
        var act = () => _manager.SaveAsync(_profile.Id, new SaveClientSettingsRequest
        {
            Playback = new PlaybackPreferencesVM { AutoSkipCredits = true, EpisodeLayout = (EpisodeLayout)42 },
            Tracks = new SaveTrackPreferencesRequest { AudioLanguage = "spa" }
        });

        await act.Should().ThrowAsync<ArgumentException>();
        _profile.AutoSkipCredits.Should().BeFalse();
        _profile.PreferredAudioLanguage.Should().Be("fra");
        await _users.DidNotReceive().UpdateProfileAsync(Arg.Any<UserProfile>());
        await _notifier.DidNotReceive().NotifyClientSettingsChangedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_profile_that_does_not_exist_is_not_found()
    {
        var get = () => _manager.GetAsync(Guid.NewGuid());
        var save = () => _manager.SaveAsync(Guid.NewGuid(), new SaveClientSettingsRequest());

        await get.Should().ThrowAsync<KeyNotFoundException>();
        await save.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_admins_save_stores_what_the_profiles_own_save_would()
    {
        var own = new UserProfile { Name = "Own" };
        var admin = new UserProfile { Name = "Admin" };
        var playback = new PlaybackPreferencesVM { AutoSkipIntro = true, UpNextOrder = UpNextOrder.InOrder, MinimumCreditsSceneSeconds = 9000 };
        _users.GetProfileByIdAsync(own.Id).Returns(own);
        _users.GetProfileByIdAsync(admin.Id).Returns(admin);
        var userManager = new UserManager(_users, Substitute.For<IUserProfileImageService>(), _notifier, Substitute.For<IClientDefaultsManager>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManager>.Instance);

        await userManager.SavePlaybackPreferencesAsync(own.Id, playback);
        await _manager.SaveAsync(admin.Id, new SaveClientSettingsRequest { Playback = playback });

        admin.PlaybackOf().Should().Be(own.PlaybackOf());
    }
}
