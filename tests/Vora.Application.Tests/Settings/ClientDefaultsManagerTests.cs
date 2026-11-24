using Vora.Application.Settings;
using Vora.Application.Streaming.Requests;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Settings;

public class ClientDefaultsManagerTests
{
    private readonly ISystemSettingsRepository _repository = Substitute.For<ISystemSettingsRepository>();
    private readonly ServerSetting _settings = new();
    private readonly ClientDefaultsManager _manager;

    public ClientDefaultsManagerTests()
    {
        _repository.GetSettingsAsync().Returns(_settings);
        _repository.GetSettingsForUpdateAsync().Returns(_settings);
        _manager = new ClientDefaultsManager(_repository);
    }

    private static SaveClientSettingsRequest Everything() => new()
    {
        Playback = new PlaybackPreferencesVM
        {
            AutoSkipIntro = true,
            AutoSkipCredits = true,
            AutoPlayNext = false,
            UpNextOrder = UpNextOrder.InOrder,
            EpisodeLayout = EpisodeLayout.List,
            MinimumCreditsSceneSeconds = 40
        },
        Tracks = new SaveTrackPreferencesRequest
        {
            AudioLanguage = "spa",
            SubtitleLanguage = "fra",
            SubtitleMode = SubtitleMode.Always,
            PreferHearingImpairedSubtitles = true
        },
        LibrarySort = new LibrarySortVM { Sort = LibrarySortField.DateAdded, Descending = true }
    };

    [Fact]
    public async Task A_server_nobody_has_set_up_gives_a_new_profile_what_a_profile_starts_with()
    {
        var started = new UserProfile { Name = "Started" };
        var given = new UserProfile { Name = "Given" };

        await _manager.ApplyToNewProfileAsync(given);

        given.PlaybackOf().Should().Be(started.PlaybackOf());
        given.TracksOf().Should().Be(started.TracksOf());
        given.LibrarySortOf().Should().Be(started.LibrarySortOf());
    }

    [Fact]
    public async Task Saved_defaults_go_to_the_next_profile()
    {
        await _manager.SaveAsync(Everything());
        var profile = new UserProfile { Name = "New" };

        await _manager.ApplyToNewProfileAsync(profile);

        profile.AutoSkipIntro.Should().BeTrue();
        profile.AutoSkipCredits.Should().BeTrue();
        profile.AutoPlayNext.Should().BeFalse();
        profile.UpNextOrder.Should().Be(UpNextOrder.InOrder);
        profile.EpisodeLayout.Should().Be(EpisodeLayout.List);
        profile.MinimumCreditsSceneSeconds.Should().Be(40);
        profile.PreferredAudioLanguage.Should().Be("spa");
        profile.PreferredSubtitleLanguage.Should().Be("fra");
        profile.SubtitleMode.Should().Be(SubtitleMode.Always);
        profile.PreferHearingImpairedSubtitles.Should().BeTrue();
        profile.LibrarySort.Should().Be(LibrarySortField.DateAdded);
        profile.LibrarySortDescending.Should().BeTrue();
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Saving_returns_what_was_stored_with_the_server_language_beside_the_tracks()
    {
        _settings.MetadataLanguage = "deu";

        var saved = await _manager.SaveAsync(Everything());

        saved.Playback.MinimumCreditsSceneSeconds.Should().Be(40);
        saved.Tracks.AudioLanguage.Should().Be("spa");
        saved.Tracks.ServerLanguage.Should().Be("deu");
        saved.LibrarySort.Descending.Should().BeTrue();
        (await _manager.GetAsync()).Should().BeEquivalentTo(saved);
    }

    [Fact]
    public async Task A_section_left_out_is_left_as_it_was()
    {
        await _manager.SaveAsync(Everything());

        await _manager.SaveAsync(new SaveClientSettingsRequest { LibrarySort = new LibrarySortVM { Sort = LibrarySortField.Title } });

        var current = await _manager.GetAsync();
        current.LibrarySort.Sort.Should().Be(LibrarySortField.Title);
        current.LibrarySort.Descending.Should().BeFalse();
        current.Playback.AutoSkipIntro.Should().BeTrue();
        current.Tracks.AudioLanguage.Should().Be("spa");
    }

    [Fact]
    public async Task A_playback_field_left_out_keeps_the_stored_value_as_the_users_own_save_does()
    {
        await _manager.SaveAsync(Everything());

        await _manager.SaveAsync(new SaveClientSettingsRequest { Playback = new PlaybackPreferencesVM { AutoSkipIntro = false, MinimumCreditsSceneSeconds = 20 } });

        var playback = (await _manager.GetAsync()).Playback;
        playback.AutoSkipIntro.Should().BeFalse();
        playback.AutoSkipCredits.Should().BeFalse();
        playback.MinimumCreditsSceneSeconds.Should().Be(20);
        playback.AutoPlayNext.Should().BeFalse();
        playback.UpNextOrder.Should().Be(UpNextOrder.InOrder);
        playback.EpisodeLayout.Should().Be(EpisodeLayout.List);
    }

    [Fact]
    public async Task A_bad_section_refuses_the_whole_save()
    {
        var request = Everything();
        request.Playback!.UpNextOrder = (UpNextOrder)99;

        var act = () => _manager.SaveAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        _settings.NewProfileAudioLanguage.Should().BeNull();
        _settings.NewProfileLibrarySort.Should().BeNull();
        await _repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task An_unknown_library_sort_or_subtitle_mode_is_refused()
    {
        var sort = () => _manager.SaveAsync(new SaveClientSettingsRequest { LibrarySort = new LibrarySortVM { Sort = (LibrarySortField)99 } });
        var mode = () => _manager.SaveAsync(new SaveClientSettingsRequest { Tracks = new SaveTrackPreferencesRequest { SubtitleMode = (SubtitleMode)99 } });

        await sort.Should().ThrowAsync<ArgumentException>();
        await mode.Should().ThrowAsync<ArgumentException>();
        await _repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task A_language_name_is_stored_as_the_code_and_the_seconds_are_kept_in_range()
    {
        await _manager.SaveAsync(new SaveClientSettingsRequest
        {
            Tracks = new SaveTrackPreferencesRequest { AudioLanguage = "Spanish" },
            Playback = new PlaybackPreferencesVM { MinimumCreditsSceneSeconds = 9000 }
        });

        _settings.NewProfileAudioLanguage.Should().Be("spa");
        _settings.NewProfileMinimumCreditsSceneSeconds.Should().Be(600);
    }

    [Fact]
    public async Task Choosing_automatic_clears_the_stored_track_defaults()
    {
        await _manager.SaveAsync(Everything());

        await _manager.SaveAsync(new SaveClientSettingsRequest { Tracks = new SaveTrackPreferencesRequest() });

        var profile = new UserProfile { Name = "New" };
        await _manager.ApplyToNewProfileAsync(profile);
        profile.TracksOf().Should().Be(new UserProfile { Name = "Fresh" }.TracksOf());
    }
}
