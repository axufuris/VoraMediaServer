using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class RememberedChoiceDecisionTests
{
    private const StreamingProfile Direct = StreamingProfile.DirectStreamPreference;
    private const StreamingProfile ClientPreference = StreamingProfile.ClientPreference;
    private const StreamingProfile Bitrate = StreamingProfile.BandwidthOptimized;

    private static TrackStreamInfoDto Audio(int streamIndex, string codec, int channels, string language, bool isDefault = false, string? title = null) => new()
    {
        Id = Guid.NewGuid(), StreamIndex = streamIndex, Codec = codec, Channels = channels, Language = language, IsDefault = isDefault, Title = title
    };

    private static SubtitleStreamInfoDto Subtitle(string codec, string language, bool isDefault = false, bool isForced = false, bool isHearingImpaired = false) => new()
    {
        Id = Guid.NewGuid(), Codec = codec, Language = language, IsDefault = isDefault, IsForced = isForced, IsHearingImpaired = isHearingImpaired
    };

    private static MediaPartStreamInfoDto Version(string resolution, TrackStreamInfoDto[] audio, SubtitleStreamInfoDto[]? subtitles = null, string? edition = null, string videoCodec = "h264") => new()
    {
        Id = Guid.NewGuid(), Container = "mkv", Resolution = resolution, Edition = edition, OverallBitrate = resolution == "2160p" ? 40_000_000 : 8_000_000,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = videoCodec, IsDefault = true }],
        AudioTracks = audio.ToList(),
        SubtitleTracks = subtitles?.ToList() ?? []
    };

    private static MediaStreamInfoDto Episode(params MediaPartStreamInfoDto[] versions) => new() { Id = Guid.NewGuid(), Parts = versions.ToList() };

    private static ProfileTitlePlaybackChoice Remembered() => new() { ProfileId = Guid.NewGuid(), DeviceId = "living-room", TitleId = Guid.NewGuid() };

    private static Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto media, ProfileTitlePlaybackChoice? remembered, StreamingProfile profile = Direct,
        string[]? audioCodecs = null, int maxHeight = 2160, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = profile });
        var device = new ClientDevice
        {
            DeviceType = "AndroidTV", MaxAudioChannels = 6,
            SupportedVideoCodecs = ["h264", "hevc"], SupportedAudioCodecs = (audioCodecs ?? ["aac", "ac3", "eac3"]).ToList(), SupportedContainers = ["mkv", "mp4", "hls"],
            SupportedHdrFormats = []
        };
        return new BestPathDecisionManager(settings).DetermineBestPathAsync(device, media, 0, "None",
            requestedAudioId: requestedAudioId, requestedSubtitleId: requestedSubtitleId, requestedMaxResolution: maxHeight,
            audioOutput: new AudioOutputRequest(6, 0, PlaysAnyLayout: false, CanSwitchTracks: true), remembered: remembered);
    }

    [Fact]
    public async Task A_remembered_language_plays_in_every_episode_although_each_has_its_own_track_ids()
    {
        var choice = Remembered();
        choice.RemembersAudio = true;
        choice.AudioLanguage = "jpn";
        choice.AudioCodec = "aac";
        choice.AudioChannels = 2;

        var japanese = Audio(2, "aac", 2, "jpn");
        var decision = await DecideAsync(Episode(Version("1080p", [Audio(1, "eac3", 6, "eng", isDefault: true), japanese])), choice);

        decision.SelectedAudioTrackId.Should().Be(japanese.Id);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Fact]
    public async Task An_episode_without_the_remembered_language_gets_the_automatic_choice()
    {
        var choice = Remembered();
        choice.RemembersAudio = true;
        choice.AudioLanguage = "jpn";
        choice.AudioCodec = "aac";
        choice.AudioChannels = 2;

        var english = Audio(1, "eac3", 6, "eng", isDefault: true);
        var decision = await DecideAsync(Episode(Version("1080p", [english, Audio(2, "aac", 2, "eng", title: "Director commentary")])), choice);

        decision.SelectedAudioTrackId.Should().Be(english.Id);
    }

    [Fact]
    public async Task Remembered_subtitles_off_turn_off_even_a_forced_subtitle()
    {
        var choice = Remembered();
        choice.Subtitle = RememberedSubtitle.Off;

        var decision = await DecideAsync(Episode(Version("1080p", [Audio(1, "aac", 2, "eng", isDefault: true)], [Subtitle("subrip", "eng", isForced: true)])), choice);

        decision.SelectedSubtitleTrackId.Should().BeNull();
    }

    [Fact]
    public async Task A_remembered_subtitle_language_finds_the_track_with_the_same_flags_and_falls_back_to_automatic_without_one()
    {
        var choice = Remembered();
        choice.Subtitle = RememberedSubtitle.Language;
        choice.SubtitleLanguage = "eng";
        choice.SubtitleIsHearingImpaired = true;

        var sdh = Subtitle("subrip", "eng", isHearingImpaired: true);
        var withSdh = await DecideAsync(Episode(Version("1080p", [Audio(1, "aac", 2, "jpn", isDefault: true)], [Subtitle("subrip", "eng"), sdh])), choice);
        withSdh.SelectedSubtitleTrackId.Should().Be(sdh.Id);

        var forced = Subtitle("subrip", "eng", isForced: true);
        var withoutSdh = await DecideAsync(Episode(Version("1080p", [Audio(1, "aac", 2, "jpn", isDefault: true)], [Subtitle("subrip", "fra"), forced])), choice);
        withoutSdh.SelectedSubtitleTrackId.Should().Be(forced.Id);
    }

    [Fact]
    public async Task A_remembered_edition_plays_although_another_edition_is_larger()
    {
        var choice = Remembered();
        choice.RemembersVersion = true;
        choice.Edition = "Director's Cut";
        choice.VersionHeight = 1080;
        choice.VideoCodec = "h264";

        var directorsCut = Version("1080p", [Audio(1, "aac", 2, "eng", isDefault: true)], edition: "Director's Cut");
        var decision = await DecideAsync(Episode(Version("2160p", [Audio(1, "aac", 2, "eng", isDefault: true)], edition: "Theatrical", videoCodec: "hevc"), directorsCut), choice);

        decision.SelectedMediaPartId.Should().Be(directorsCut.Id);
    }

    [Theory]
    [InlineData(Direct)]
    [InlineData(Bitrate)]
    public async Task Direct_and_Bitrate_play_another_track_in_the_remembered_language_when_the_remembered_format_would_be_converted(StreamingProfile profile)
    {
        var choice = Remembered();
        choice.RemembersAudio = true;
        choice.AudioLanguage = "eng";
        choice.AudioCodec = "truehd";
        choice.AudioChannels = 8;

        var playable = Audio(2, "ac3", 6, "eng");
        var decision = await DecideAsync(Episode(Version("1080p", [Audio(1, "truehd", 8, "eng", isDefault: true), playable, Audio(3, "aac", 2, "fra")])), choice, profile);

        decision.SelectedAudioTrackId.Should().Be(playable.Id);
        decision.AudioStrategy.Should().NotBe("Transcode");
    }

    [Fact]
    public async Task Client_Preference_converts_the_remembered_format_rather_than_play_another()
    {
        var choice = Remembered();
        choice.RemembersAudio = true;
        choice.AudioLanguage = "eng";
        choice.AudioCodec = "truehd";
        choice.AudioChannels = 8;

        var trueHd = Audio(1, "truehd", 8, "eng", isDefault: true);
        var decision = await DecideAsync(Episode(Version("1080p", [trueHd, Audio(2, "ac3", 6, "eng")])), choice, ClientPreference);

        decision.SelectedAudioTrackId.Should().Be(trueHd.Id);
        decision.AudioStrategy.Should().Be("Transcode");
    }

    [Fact]
    public async Task A_track_picked_for_this_play_outranks_the_remembered_one()
    {
        var choice = Remembered();
        choice.RemembersAudio = true;
        choice.AudioLanguage = "jpn";
        choice.AudioCodec = "aac";
        choice.AudioChannels = 2;

        var english = Audio(1, "eac3", 6, "eng", isDefault: true);
        var decision = await DecideAsync(Episode(Version("1080p", [english, Audio(2, "aac", 2, "jpn")])), choice, requestedAudioId: english.Id);

        decision.SelectedAudioTrackId.Should().Be(english.Id);
    }

    [Fact]
    public async Task A_burned_in_subtitle_does_not_count_against_a_remembered_version()
    {
        var choice = Remembered();
        choice.RemembersVersion = true;
        choice.VersionHeight = 2160;
        choice.VideoCodec = "hevc";

        var image = Subtitle("hdmv_pgs_subtitle", "eng");
        var uhd = Version("2160p", [Audio(1, "aac", 2, "eng", isDefault: true)], [image], videoCodec: "hevc");
        var hd = Version("1080p", [Audio(1, "aac", 2, "eng", isDefault: true)], [Subtitle("hdmv_pgs_subtitle", "eng")]);
        var decision = await DecideAsync(Episode(uhd, hd), choice, requestedSubtitleId: image.Id);

        decision.SelectedMediaPartId.Should().Be(uhd.Id);
        decision.RequiresSubtitleBurnIn.Should().BeTrue();
    }
}
