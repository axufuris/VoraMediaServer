using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Requests;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class TrackPreferenceManagerTests
{
    private readonly UserProfile _profile = new() { Name = "Viewer", UserId = Guid.NewGuid() };
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();

    public TrackPreferenceManagerTests()
    {
        _users.GetProfileByIdAsync(_profile.Id).Returns(_profile);
        _settings.GetSettingsAsync().Returns(new ServerSetting { MetadataLanguage = "deu", AutoEnableSubtitlesForForeignAudio = true });
    }

    private TrackPreferenceManager Manager() => new(_users, _settings);

    [Fact]
    public async Task A_new_profile_is_automatic_and_is_told_what_automatic_means_on_this_server()
    {
        var preferences = await Manager().GetAsync(_profile.Id);

        preferences.AudioLanguage.Should().BeNull();
        preferences.SubtitleLanguage.Should().BeNull();
        preferences.SubtitleMode.Should().BeNull();
        preferences.PreferHearingImpairedSubtitles.Should().BeFalse();
        preferences.ServerLanguage.Should().Be("deu");
        preferences.ServerSubtitleMode.Should().Be(SubtitleMode.Smart);
    }

    [Fact]
    public async Task Saved_languages_are_stored_as_the_code_the_server_matches_on()
    {
        var saved = await Manager().SaveAsync(_profile.Id, new SaveTrackPreferencesRequest
        {
            AudioLanguage = "ja", SubtitleLanguage = "English", SubtitleMode = SubtitleMode.Always, PreferHearingImpairedSubtitles = true
        });

        saved.AudioLanguage.Should().Be("jpn");
        saved.SubtitleLanguage.Should().Be("eng");
        saved.SubtitleMode.Should().Be(SubtitleMode.Always);
        saved.PreferHearingImpairedSubtitles.Should().BeTrue();
        _profile.PreferredAudioLanguage.Should().Be("jpn");
        await _users.Received(1).UpdateProfileAsync(_profile);
    }

    [Fact]
    public async Task Saving_blanks_puts_the_profile_back_on_automatic()
    {
        _profile.PreferredAudioLanguage = "jpn";
        _profile.SubtitleMode = SubtitleMode.Off;

        var saved = await Manager().SaveAsync(_profile.Id, new SaveTrackPreferencesRequest { AudioLanguage = " ", SubtitleLanguage = null, SubtitleMode = null });

        saved.AudioLanguage.Should().BeNull();
        saved.SubtitleMode.Should().BeNull();
        _profile.PreferredAudioLanguage.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_profile_is_not_found()
    {
        var get = () => Manager().GetAsync(Guid.NewGuid());

        await get.Should().ThrowAsync<KeyNotFoundException>();
    }
}
