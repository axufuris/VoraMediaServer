using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class DirectPlayCaseTests
{
    private static ClientDevice Browser() => new()
    {
        DeviceType = "Browser", MaxAudioChannels = 2, MaxVideoBitDepth = 10,
        SupportedVideoCodecs = ["h264", "hevc", "vp9", "av1"], SupportedAudioCodecs = ["aac", "ac3", "eac3", "opus", "flac", "mp3"],
        SupportedContainers = ["mp4", "webm", "mkv"], SupportedHdrFormats = []
    };

    private static ClientDevice ShieldTv() => new()
    {
        DeviceType = "AndroidTV", MaxAudioChannels = 6, MaxVideoBitDepth = 10,
        SupportedVideoCodecs = ["h264", "hevc", "vp9", "av1"], SupportedAudioCodecs = ["aac", "ac3", "eac3", "opus", "flac", "mp3"],
        SupportedContainers = ["mp4", "webm", "mkv", "hls"], SupportedHdrFormats = ["HDR10", "HLG"]
    };

    private static readonly AudioOutputRequest BrowserOutput = new(2, 0, PlaysAnyLayout: true, CanSwitchTracks: false);
    private static readonly AudioOutputRequest TvOutput = new(6, 0, PlaysAnyLayout: false, CanSwitchTracks: true);

    private static MediaStreamInfoDto Video(string container, string codec, int width, int height, long bitrate = 20_000_000) => new()
    {
        Id = Guid.NewGuid(),
        Parts =
        [
            new MediaPartStreamInfoDto
            {
                Id = Guid.NewGuid(), Container = container, OverallBitrate = bitrate,
                VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = codec, IsDefault = true, Width = width, Height = height, FrameRate = 29.97, BitDepth = 8, StreamIndex = 0 }],
                AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "aac", Channels = 2, IsDefault = true, Language = "eng", StreamIndex = 1 }],
            }
        ]
    };

    private static Task<StreamDecisionDto> DecideAsync(ClientDevice client, MediaStreamInfoDto media, StreamingProfile profile, AudioOutputRequest output, int maxHeight = 0, int maxWidth = 0)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = profile });
        return new BestPathDecisionManager(settings).DetermineBestPathAsync(client, media, 0, "None",
            requestedMaxResolution: maxHeight, requestedMaxWidth: maxWidth, audioOutput: output);
    }

    public static TheoryData<StreamingProfile> Profiles() => new(Enum.GetValues<StreamingProfile>());

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task A_cinema_width_4k_file_plays_as_it_is_where_the_client_set_no_width(StreamingProfile profile)
    {
        (await DecideAsync(Browser(), Video("mp4", "h264", 4096, 2160), profile, BrowserOutput)).Strategy.Should().Be(StreamStrategy.DirectPlay);
        (await DecideAsync(ShieldTv(), Video("mkv", "hevc", 4096, 1716), profile, TvOutput, maxHeight: 2160)).Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task A_picture_taller_than_4k_or_wider_than_a_width_the_client_set_is_still_converted(StreamingProfile profile)
    {
        (await DecideAsync(Browser(), Video("mp4", "h264", 4096, 2304), profile, BrowserOutput)).VideoStrategy.Should().Be("Transcode");
        (await DecideAsync(Browser(), Video("mp4", "h264", 4096, 2160), profile, BrowserOutput, maxHeight: 2160, maxWidth: 3840)).VideoStrategy.Should().Be("Transcode");
        (await DecideAsync(ShieldTv(), Video("mkv", "hevc", 3840, 2160), profile, TvOutput, maxHeight: 1080)).VideoStrategy.Should().Be("Transcode");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task A_portrait_video_is_measured_on_its_long_and_short_sides(StreamingProfile profile)
    {
        (await DecideAsync(Browser(), Video("mp4", "hevc", 2160, 3840), profile, BrowserOutput)).Strategy.Should().Be(StreamStrategy.DirectPlay);
        (await DecideAsync(Browser(), Video("mp4", "h264", 1080, 1920, 8_000_000), profile, BrowserOutput, maxHeight: 1080, maxWidth: 1920)).Strategy.Should().Be(StreamStrategy.DirectPlay);
        (await DecideAsync(Browser(), Video("mp4", "h264", 1080, 1920, 8_000_000), profile, BrowserOutput, maxHeight: 720, maxWidth: 1280)).VideoStrategy.Should().Be("Transcode");
    }

    [Theory]
    [InlineData("m4v")]
    [InlineData("mov")]
    [InlineData("M4V")]
    public async Task An_m4v_or_quicktime_file_plays_as_it_is_on_a_client_that_plays_mp4(string container)
    {
        foreach (var profile in Enum.GetValues<StreamingProfile>())
        {
            var web = await DecideAsync(Browser(), Video(container, "h264", 1920, 1080, 8_000_000), profile, BrowserOutput);
            var tv = await DecideAsync(ShieldTv(), Video(container, "hevc", 1920, 1080, 8_000_000), profile, TvOutput);

            (web.Strategy, web.TargetContainer).Should().Be((StreamStrategy.DirectPlay, container.ToLowerInvariant()));
            tv.Strategy.Should().Be(StreamStrategy.DirectPlay);
        }
    }

    [Fact]
    public async Task A_quicktime_file_is_still_repackaged_for_a_client_without_mp4_and_converted_for_a_codec_it_lacks()
    {
        var mkvOnly = Browser();
        mkvOnly.SupportedContainers = ["mkv"];

        (await DecideAsync(mkvOnly, Video("mov", "h264", 1920, 1080, 8_000_000), StreamingProfile.DirectStreamPreference, BrowserOutput)).Strategy.Should().Be(StreamStrategy.Remux);
        (await DecideAsync(Browser(), Video("mov", "prores", 1920, 1080, 8_000_000), StreamingProfile.DirectStreamPreference, BrowserOutput)).VideoStrategy.Should().Be("Transcode");
    }

    private static ClientDevice TvListing(string container)
    {
        var tv = ShieldTv();
        tv.SupportedContainers = [container];
        return tv;
    }

    [Theory]
    [InlineData("mp4", "3gp")]
    [InlineData("mov", "mp4")]
    [InlineData("m4v", "mov")]
    [InlineData("3gp", "m4v")]
    [InlineData("mpeg", "mpg")]
    [InlineData("vob", "mpeg")]
    [InlineData("mpg", "vob")]
    [InlineData("ts", "ts")]
    [InlineData("m2ts", "mts")]
    [InlineData("mts", "m2ts")]
    [InlineData("MP4", "Mov")]
    public async Task A_client_that_lists_one_name_of_a_format_plays_every_name_of_it(string listed, string file)
    {
        var decision = await DecideAsync(TvListing(listed), Video(file, "h264", 1920, 1080, 8_000_000), StreamingProfile.DirectStreamPreference, TvOutput);

        (decision.Strategy, decision.TargetContainer).Should().Be((StreamStrategy.DirectPlay, file.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("ts", "m2ts")]
    [InlineData("m2ts", "ts")]
    [InlineData("ts", "mts")]
    [InlineData("mkv", "webm")]
    [InlineData("webm", "mkv")]
    [InlineData("mp4", "mpg")]
    [InlineData("mpeg", "ts")]
    public async Task Formats_that_not_every_player_reads_alike_are_kept_apart(string listed, string file)
    {
        var decision = await DecideAsync(TvListing(listed), Video(file, "h264", 1920, 1080, 8_000_000), StreamingProfile.DirectStreamPreference, TvOutput);

        decision.Strategy.Should().Be(StreamStrategy.Remux);
    }

    private static SubtitleStreamInfoDto Subtitle(string language, bool forced = false, bool isDefault = false) => new()
    {
        Id = Guid.NewGuid(), Codec = "hdmv_pgs_subtitle", Language = language, IsForced = forced, IsDefault = isDefault
    };

    private static readonly SubtitlePreference EnglishDefault = new(SubtitleMode.Default, "eng", false);

    [Fact]
    public void A_forced_subtitle_for_another_dub_is_not_turned_on()
    {
        var frenchForced = Subtitle("fre", forced: true);

        BestPathDecisionManager.PickAutomaticSubtitle([frenchForced, Subtitle("eng")], "eng", EnglishDefault).Should().BeNull();
        BestPathDecisionManager.PickAutomaticSubtitle([frenchForced, Subtitle("eng")], "eng", EnglishDefault with { Mode = SubtitleMode.ForcedOnly }).Should().BeNull();
    }

    [Fact]
    public void A_forced_subtitle_in_the_language_being_heard_or_with_no_language_is_still_turned_on()
    {
        var spanishForced = Subtitle("spa", forced: true);
        var untaggedForced = Subtitle("und", forced: true);

        BestPathDecisionManager.PickAutomaticSubtitle([Subtitle("fre", forced: true), spanishForced], "spa", EnglishDefault).Should().Be(spanishForced);
        BestPathDecisionManager.PickAutomaticSubtitle([Subtitle("fre", forced: true), untaggedForced], "eng", EnglishDefault).Should().Be(untaggedForced);
    }

    [Fact]
    public void A_forced_subtitle_in_the_viewers_language_still_comes_first()
    {
        var englishForced = Subtitle("eng", forced: true);

        BestPathDecisionManager.PickAutomaticSubtitle([Subtitle("spa", forced: true), englishForced], "spa", EnglishDefault).Should().Be(englishForced);
    }
}
