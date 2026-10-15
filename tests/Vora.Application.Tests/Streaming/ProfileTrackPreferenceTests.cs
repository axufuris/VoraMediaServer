using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class ProfileTrackPreferenceTests
{
    private static TrackStreamInfoDto Audio(int streamIndex, string codec, int channels, string language, bool isDefault = false, string? title = null) => new()
    {
        Id = Guid.NewGuid(), StreamIndex = streamIndex, Codec = codec, Channels = channels, Language = language, IsDefault = isDefault, Title = title
    };

    private static SubtitleStreamInfoDto Subtitle(string language, bool isDefault = false, bool isForced = false, bool isHearingImpaired = false) => new()
    {
        Id = Guid.NewGuid(), Codec = "subrip", Language = language, IsDefault = isDefault, IsForced = isForced, IsHearingImpaired = isHearingImpaired
    };

    private static MediaStreamInfoDto Film(TrackStreamInfoDto[] audio, SubtitleStreamInfoDto[]? subtitles = null) => new()
    {
        Id = Guid.NewGuid(),
        Parts =
        [
            new MediaPartStreamInfoDto
            {
                Id = Guid.NewGuid(), Container = "mkv", Resolution = "1080p", OverallBitrate = 8_000_000,
                VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "h264", IsDefault = true }],
                AudioTracks = audio.ToList(),
                SubtitleTracks = subtitles?.ToList() ?? []
            }
        ]
    };

    private static TrackPreferences Prefer(string? audio = null, SubtitleMode? mode = null, string? subtitles = null, bool sdh = false) =>
        new(audio, mode, subtitles, sdh);

    private static Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto media, TrackPreferences? preferences, bool serverForeignAudioRule = false,
        ProfileTitlePlaybackChoice? remembered = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting
        {
            StreamingProfile = StreamingProfile.DirectStreamPreference,
            MetadataLanguage = "eng",
            AutoEnableSubtitlesForForeignAudio = serverForeignAudioRule
        });
        var device = new ClientDevice
        {
            DeviceType = "AndroidTV", MaxAudioChannels = 6,
            SupportedVideoCodecs = ["h264", "hevc"], SupportedAudioCodecs = ["aac", "ac3", "eac3"], SupportedContainers = ["mkv", "mp4", "hls"],
            SupportedHdrFormats = []
        };
        return new BestPathDecisionManager(settings).DetermineBestPathAsync(device, media, 0, "None",
            requestedAudioId: requestedAudioId, requestedSubtitleId: requestedSubtitleId,
            audioOutput: new AudioOutputRequest(6, 0, PlaysAnyLayout: false, CanSwitchTracks: true), remembered: remembered, preferences: preferences);
    }

    [Fact]
    public async Task A_preferred_audio_language_plays_when_the_file_has_it()
    {
        var japanese = Audio(2, "aac", 2, "jpn");
        var decision = await DecideAsync(Film([Audio(1, "eac3", 6, "eng", isDefault: true), japanese]), Prefer(audio: "jpn"));

        decision.SelectedAudioTrackId.Should().Be(japanese.Id);
    }

    [Fact]
    public async Task The_best_track_in_the_preferred_language_is_ranked_as_usual()
    {
        var surround = Audio(3, "eac3", 6, "jpn");
        var decision = await DecideAsync(Film([Audio(1, "eac3", 6, "eng", isDefault: true), Audio(2, "aac", 2, "jpn"), surround]), Prefer(audio: "jpn"));

        decision.SelectedAudioTrackId.Should().Be(surround.Id);
    }

    [Fact]
    public async Task Without_the_preferred_language_the_files_default_track_plays()
    {
        var english = Audio(1, "eac3", 6, "eng", isDefault: true);
        var decision = await DecideAsync(Film([english, Audio(2, "aac", 2, "fra")]), Prefer(audio: "jpn"));

        decision.SelectedAudioTrackId.Should().Be(english.Id);
    }

    [Fact]
    public async Task A_commentary_in_the_preferred_language_is_never_picked_for_it()
    {
        var english = Audio(1, "eac3", 6, "eng", isDefault: true);
        var decision = await DecideAsync(Film([english, Audio(2, "aac", 2, "jpn", title: "Director commentary")]), Prefer(audio: "jpn"));

        decision.SelectedAudioTrackId.Should().Be(english.Id);
    }

    [Fact]
    public async Task A_remembered_pick_for_the_title_beats_the_profile_language()
    {
        var english = Audio(1, "eac3", 6, "eng", isDefault: true);
        var remembered = new ProfileTitlePlaybackChoice
        {
            ProfileId = Guid.NewGuid(), DeviceId = "living-room", TitleId = Guid.NewGuid(),
            RemembersAudio = true, AudioLanguage = "eng", AudioCodec = "eac3", AudioChannels = 6
        };

        var decision = await DecideAsync(Film([english, Audio(2, "aac", 2, "jpn")]), Prefer(audio: "jpn"), remembered: remembered);

        decision.SelectedAudioTrackId.Should().Be(english.Id);
    }

    [Fact]
    public async Task A_track_asked_for_by_the_player_beats_the_profile_language()
    {
        var english = Audio(1, "eac3", 6, "eng", isDefault: true);
        var decision = await DecideAsync(Film([english, Audio(2, "aac", 2, "jpn")]), Prefer(audio: "jpn"), requestedAudioId: english.Id);

        decision.SelectedAudioTrackId.Should().Be(english.Id);
    }

    [Fact]
    public async Task Smart_subtitles_follow_the_language_that_plays_not_the_files_default()
    {
        var englishSubtitles = Subtitle("eng");
        var media = Film([Audio(1, "eac3", 6, "eng", isDefault: true), Audio(2, "aac", 2, "jpn")], [englishSubtitles]);

        var japaneseAudio = await DecideAsync(media, Prefer(audio: "jpn", mode: SubtitleMode.Smart));
        var defaultAudio = await DecideAsync(media, Prefer(mode: SubtitleMode.Smart));

        japaneseAudio.SelectedSubtitleTrackId.Should().Be(englishSubtitles.Id);
        defaultAudio.SelectedSubtitleTrackId.Should().BeNull();
    }

    [Fact]
    public async Task Smart_uses_the_profiles_subtitle_language_rather_than_the_servers()
    {
        var spanish = Subtitle("spa");
        var media = Film([Audio(1, "aac", 2, "jpn", isDefault: true)], [Subtitle("eng"), spanish]);

        var decision = await DecideAsync(media, Prefer(mode: SubtitleMode.Smart, subtitles: "spa"));

        decision.SelectedSubtitleTrackId.Should().Be(spanish.Id);
    }

    [Fact]
    public async Task Default_follows_only_the_files_flags_even_for_foreign_audio()
    {
        var media = Film([Audio(1, "aac", 2, "jpn", isDefault: true)], [Subtitle("eng")]);

        var decision = await DecideAsync(media, Prefer(mode: SubtitleMode.Default), serverForeignAudioRule: true);

        decision.SelectedSubtitleTrackId.Should().BeNull();
    }

    [Fact]
    public async Task Off_turns_off_even_a_forced_subtitle()
    {
        var media = Film([Audio(1, "aac", 2, "eng", isDefault: true)], [Subtitle("eng", isForced: true)]);

        var decision = await DecideAsync(media, Prefer(mode: SubtitleMode.Off));

        decision.SelectedSubtitleTrackId.Should().BeNull();
    }

    [Fact]
    public async Task Only_forced_plays_a_forced_track_and_never_a_full_one()
    {
        var forced = Subtitle("eng", isForced: true);
        var withForced = await DecideAsync(Film([Audio(1, "aac", 2, "jpn", isDefault: true)], [Subtitle("eng", isDefault: true), forced]), Prefer(mode: SubtitleMode.ForcedOnly));
        var withoutForced = await DecideAsync(Film([Audio(1, "aac", 2, "jpn", isDefault: true)], [Subtitle("eng", isDefault: true)]), Prefer(mode: SubtitleMode.ForcedOnly));

        withForced.SelectedSubtitleTrackId.Should().Be(forced.Id);
        withoutForced.SelectedSubtitleTrackId.Should().BeNull();
    }

    [Fact]
    public async Task Always_plays_the_full_subtitles_even_when_the_audio_is_in_that_language()
    {
        var full = Subtitle("eng");
        var media = Film([Audio(1, "aac", 2, "eng", isDefault: true)], [Subtitle("eng", isForced: true), full]);

        var decision = await DecideAsync(media, Prefer(mode: SubtitleMode.Always));

        decision.SelectedSubtitleTrackId.Should().Be(full.Id);
    }

    [Fact]
    public async Task Always_without_a_track_in_the_language_falls_back_to_the_files_flags()
    {
        var flagged = Subtitle("fra", isDefault: true);
        var media = Film([Audio(1, "aac", 2, "eng", isDefault: true)], [flagged]);

        var decision = await DecideAsync(media, Prefer(mode: SubtitleMode.Always));

        decision.SelectedSubtitleTrackId.Should().Be(flagged.Id);
    }

    [Fact]
    public async Task Preferring_SDH_picks_the_hearing_impaired_track_and_otherwise_it_is_a_last_resort()
    {
        var plain = Subtitle("eng");
        var sdh = Subtitle("eng", isHearingImpaired: true);
        var media = Film([Audio(1, "aac", 2, "eng", isDefault: true)], [plain, sdh]);

        var preferringSdh = await DecideAsync(media, Prefer(mode: SubtitleMode.Always, sdh: true));
        var notPreferring = await DecideAsync(media, Prefer(mode: SubtitleMode.Always));

        preferringSdh.SelectedSubtitleTrackId.Should().Be(sdh.Id);
        notPreferring.SelectedSubtitleTrackId.Should().Be(plain.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_automatic_mode_follows_the_servers_foreign_audio_setting(bool serverRule)
    {
        var english = Subtitle("eng");
        var media = Film([Audio(1, "aac", 2, "jpn", isDefault: true)], [english]);

        var decision = await DecideAsync(media, Prefer(), serverForeignAudioRule: serverRule);

        decision.SelectedSubtitleTrackId.Should().Be(serverRule ? english.Id : null);
    }

    [Fact]
    public async Task A_remembered_subtitle_pick_beats_the_profile_mode()
    {
        var remembered = new ProfileTitlePlaybackChoice
        {
            ProfileId = Guid.NewGuid(), DeviceId = "living-room", TitleId = Guid.NewGuid(),
            Subtitle = RememberedSubtitle.Language, SubtitleLanguage = "eng"
        };
        var english = Subtitle("eng");
        var media = Film([Audio(1, "aac", 2, "eng", isDefault: true)], [english]);

        var decision = await DecideAsync(media, Prefer(mode: SubtitleMode.Off), remembered: remembered);

        decision.SelectedSubtitleTrackId.Should().Be(english.Id);
    }
}
