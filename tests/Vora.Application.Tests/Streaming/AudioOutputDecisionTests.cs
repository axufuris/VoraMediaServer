using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class AudioOutputDecisionTests
{
    private const StreamingProfile Direct = StreamingProfile.DirectStreamPreference;
    private const StreamingProfile ClientPreference = StreamingProfile.ClientPreference;
    private const StreamingProfile Bitrate = StreamingProfile.BandwidthOptimized;

    private static readonly string[] TvCodecs = ["aac", "ac3", "eac3"];

    private static TrackStreamInfoDto Audio(int streamIndex, string codec, int channels, bool isDefault = false) => new()
    {
        Id = Guid.NewGuid(), StreamIndex = streamIndex, Codec = codec, Channels = channels, IsDefault = isDefault, Language = "eng"
    };

    private static MediaPartStreamInfoDto Version(string resolution, string videoCodec, string container, params TrackStreamInfoDto[] audio) => new()
    {
        Id = Guid.NewGuid(), Container = container, Resolution = resolution, OverallBitrate = resolution == "2160p" ? 50_000_000 : 8_000_000,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = videoCodec, IsDefault = true }],
        AudioTracks = audio.ToList()
    };

    private static MediaStreamInfoDto Film(params TrackStreamInfoDto[] audio) => Film(Version("1080p", "h264", "mkv", audio));

    private static MediaStreamInfoDto Film(params MediaPartStreamInfoDto[] versions) => new() { Id = Guid.NewGuid(), Parts = versions.ToList() };

    private static Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto film, StreamingProfile profile, AudioOutputRequest output, string deviceType, string[]? audioCodecs = null, string[]? videoCodecs = null, int bitrateLimitKbps = 0)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = profile });
        var device = new ClientDevice
        {
            DeviceType = deviceType, MaxAudioChannels = output.PickedChannels > 0 ? Math.Min(output.PickedChannels, output.DetectedChannels) : output.DetectedChannels,
            SupportedVideoCodecs = (videoCodecs ?? ["h264", "hevc"]).ToList(), SupportedAudioCodecs = (audioCodecs ?? TvCodecs).ToList(),
            SupportedContainers = ["mp4", "mkv", "hls"]
        };
        return new BestPathDecisionManager(settings).DetermineBestPathAsync(device, film, bitrateLimitKbps, bitrateLimitKbps > 0 ? "Client Limit" : "None",
            requestedSubtitleId: Guid.Empty, requestedMaxResolution: 2160, audioOutput: output);
    }

    private static Task<StreamDecisionDto> TvAppAsync(MediaStreamInfoDto film, StreamingProfile profile, int detected, int picked = 0, string[]? audioCodecs = null, string[]? videoCodecs = null, int bitrateLimitKbps = 0) =>
        DecideAsync(film, profile, new AudioOutputRequest(detected, picked, PlaysAnyLayout: false, CanSwitchTracks: true), "AndroidTV", audioCodecs, videoCodecs, bitrateLimitKbps);

    private static Task<StreamDecisionDto> BrowserAsync(MediaStreamInfoDto film, StreamingProfile profile, int detected, int picked = 0, string[]? audioCodecs = null) =>
        DecideAsync(film, profile, new AudioOutputRequest(detected, picked, PlaysAnyLayout: true, CanSwitchTracks: false), "Browser", audioCodecs);

    [Fact]
    public async Task A_TV_app_plays_the_file_directly_and_is_told_which_track_to_switch_to()
    {
        var surround = Audio(2, "ac3", 6);
        var decision = await TvAppAsync(Film(Audio(1, "aac", 2, isDefault: true), surround), Direct, detected: 6);

        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
        decision.SelectedAudioTrackId.Should().Be(surround.Id);
        decision.SelectedAudioTrackPosition.Should().Be(1);
    }

    [Fact]
    public async Task The_track_position_follows_the_order_of_the_streams_in_the_file()
    {
        var surround = Audio(1, "ac3", 6);
        var decision = await TvAppAsync(Film(Audio(2, "aac", 2, isDefault: true), surround), Direct, detected: 6);

        decision.SelectedAudioTrackId.Should().Be(surround.Id);
        decision.SelectedAudioTrackPosition.Should().Be(0);
    }

    [Fact]
    public async Task Under_Direct_a_lower_pick_plays_a_surround_only_file_as_it_is()
    {
        var decision = await TvAppAsync(Film(Audio(1, "eac3", 6, isDefault: true)), Direct, detected: 6, picked: 2);

        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Theory]
    [InlineData(ClientPreference)]
    [InlineData(Bitrate)]
    public async Task Under_the_other_profiles_a_lower_pick_converts_a_surround_only_file_for_a_TV_app(StreamingProfile profile)
    {
        var decision = await TvAppAsync(Film(Audio(1, "eac3", 6, isDefault: true)), profile, detected: 6, picked: 2);

        decision.AudioStrategy.Should().Be("Transcode");
        decision.VideoStrategy.Should().Be("DirectStream");
        decision.TargetAudioChannels.Should().Be(2);
    }

    [Theory]
    [InlineData(Direct)]
    [InlineData(ClientPreference)]
    [InlineData(Bitrate)]
    public async Task A_pick_above_what_was_detected_plays_the_surround_track_directly(StreamingProfile profile)
    {
        var surround = Audio(2, "eac3", 6);
        var decision = await TvAppAsync(Film(Audio(1, "aac", 2, isDefault: true), surround), profile, detected: 2, picked: 6);

        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
        decision.SelectedAudioTrackId.Should().Be(surround.Id);
    }

    [Fact]
    public async Task Under_Direct_a_pick_above_what_was_detected_never_converts_to_reach_it()
    {
        var stereo = Audio(2, "aac", 2);
        var decision = await TvAppAsync(Film(Audio(1, "truehd", 8, isDefault: true), stereo), Direct, detected: 2, picked: 6);

        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
        decision.SelectedAudioTrackId.Should().Be(stereo.Id);
    }

    [Theory]
    [InlineData(ClientPreference)]
    [InlineData(Bitrate)]
    public async Task The_other_profiles_convert_the_audio_alone_to_keep_surround(StreamingProfile profile)
    {
        var lossless = Audio(1, "truehd", 8, isDefault: true);
        var decision = await TvAppAsync(Film(lossless, Audio(2, "aac", 2)), profile, detected: 6);

        decision.SelectedAudioTrackId.Should().Be(lossless.Id);
        decision.AudioStrategy.Should().Be("Transcode");
        decision.VideoStrategy.Should().Be("DirectStream");
        decision.TargetAudioChannels.Should().Be(6);
    }

    [Fact]
    public async Task Converted_surround_is_Dolby_Digital_for_an_app_that_plays_it_and_never_more_than_5_1()
    {
        var decision = await TvAppAsync(Film(Audio(1, "truehd", 8, isDefault: true)), ClientPreference, detected: 8);

        decision.AudioStrategy.Should().Be("Transcode");
        decision.TargetAudioCodec.Should().Be("ac3");
        decision.TargetAudioChannels.Should().Be(6);
    }

    [Fact]
    public async Task Converted_surround_is_AAC_for_an_app_that_does_not_play_Dolby_Digital()
    {
        var decision = await TvAppAsync(Film(Audio(1, "truehd", 8, isDefault: true)), ClientPreference, detected: 8, audioCodecs: ["aac"]);

        decision.TargetAudioCodec.Should().Be("aac");
        decision.TargetAudioChannels.Should().Be(8);
    }

    [Fact]
    public async Task Converted_surround_is_AAC_for_a_browser_even_when_it_plays_Dolby_Digital()
    {
        var film = Film(Version("1080p", "h264", "mkv", Audio(1, "dts", 6, isDefault: true)));
        var decision = await BrowserAsync(film, ClientPreference, detected: 6, audioCodecs: ["aac", "ac3", "eac3"]);

        decision.TargetAudioCodec.Should().Be("aac");
        decision.TargetAudioChannels.Should().Be(6);
    }

    [Fact]
    public async Task Converted_stereo_is_AAC()
    {
        var decision = await TvAppAsync(Film(Audio(1, "eac3", 6, isDefault: true)), ClientPreference, detected: 6, picked: 2);

        decision.TargetAudioCodec.Should().Be("aac");
        decision.TargetAudioChannels.Should().Be(2);
    }

    [Theory]
    [InlineData(Direct, 0)]
    [InlineData(ClientPreference, 2)]
    [InlineData(Bitrate, 2)]
    public async Task A_browser_plays_surround_directly_on_stereo_speakers_because_it_mixes_down_itself(StreamingProfile profile, int picked)
    {
        var decision = await BrowserAsync(Film(Audio(1, "eac3", 6, isDefault: true)), profile, detected: 2, picked);

        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Fact]
    public async Task A_browser_gets_the_stream_repackaged_when_the_default_track_is_not_the_first_in_the_file()
    {
        var flagged = Audio(2, "eac3", 6, isDefault: true);
        var decision = await BrowserAsync(Film(Audio(1, "aac", 2), flagged), Direct, detected: 6);

        decision.Strategy.Should().Be(StreamStrategy.Remux);
        decision.SelectedAudioTrackId.Should().Be(flagged.Id);
        decision.TargetContainer.Should().Be("hls");
    }

    [Fact]
    public async Task A_browser_asked_for_stereo_gets_the_stereo_track_repackaged_rather_than_the_default_surround()
    {
        var stereo = Audio(2, "aac", 2);
        var decision = await BrowserAsync(Film(Audio(1, "ac3", 6, isDefault: true), stereo), Direct, detected: 6, picked: 2);

        decision.Strategy.Should().Be(StreamStrategy.Remux);
        decision.SelectedAudioTrackId.Should().Be(stereo.Id);
    }

    [Fact]
    public async Task The_Bitrate_profile_plays_the_larger_version_when_it_fits_the_bitrate_and_plays_as_it_is()
    {
        var larger = Version("2160p", "hevc", "mkv", Audio(1, "truehd", 8, isDefault: true));
        var film = Film(larger, Version("1080p", "h264", "mkv", Audio(1, "aac", 2, isDefault: true)));
        string[] codecs = ["aac", "truehd"];

        var unlimited = await TvAppAsync(film, Bitrate, detected: 8, audioCodecs: codecs);
        var roomy = await TvAppAsync(film, Bitrate, detected: 8, audioCodecs: codecs, bitrateLimitKbps: 60000);

        unlimited.SelectedMediaPartId.Should().Be(larger.Id);
        unlimited.Strategy.Should().Be(StreamStrategy.DirectPlay);
        roomy.SelectedMediaPartId.Should().Be(larger.Id);
        roomy.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Fact]
    public async Task The_Bitrate_profile_goes_to_the_smaller_version_when_the_larger_one_is_over_the_bitrate()
    {
        var smaller = Version("1080p", "h264", "mkv", Audio(1, "aac", 2, isDefault: true));
        var film = Film(Version("2160p", "hevc", "mkv", Audio(1, "aac", 2, isDefault: true)), smaller);

        var decision = await TvAppAsync(film, Bitrate, detected: 2, bitrateLimitKbps: 20000);

        decision.SelectedMediaPartId.Should().Be(smaller.Id);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Fact]
    public async Task The_Bitrate_profile_goes_to_the_smaller_version_when_the_larger_one_cannot_play_as_it_is()
    {
        var smaller = Version("1080p", "h264", "mkv", Audio(1, "aac", 2, isDefault: true));
        var film = Film(Version("2160p", "hevc", "mkv", Audio(1, "aac", 2, isDefault: true)), smaller);

        var decision = await TvAppAsync(film, Bitrate, detected: 2, videoCodecs: ["h264"]);

        decision.SelectedMediaPartId.Should().Be(smaller.Id);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Fact]
    public async Task Client_Preference_converts_the_larger_version_where_the_Bitrate_profile_would_go_smaller()
    {
        var larger = Version("2160p", "hevc", "mkv", Audio(1, "aac", 2, isDefault: true));
        var film = Film(larger, Version("1080p", "h264", "mkv", Audio(1, "aac", 2, isDefault: true)));

        var decision = await TvAppAsync(film, ClientPreference, detected: 2, videoCodecs: ["h264"]);

        decision.SelectedMediaPartId.Should().Be(larger.Id);
        decision.VideoStrategy.Should().Be("Transcode");
    }

    [Fact]
    public async Task A_browser_plays_another_track_rather_than_re_encode_HEVC_video_for_the_sake_of_the_audio()
    {
        var playable = Audio(2, "aac", 2);
        var film = Film(Version("2160p", "hevc", "mkv", Audio(1, "eac3", 6, isDefault: true), playable));
        var decision = await BrowserAsync(film, ClientPreference, detected: 6, audioCodecs: ["aac"]);

        decision.SelectedAudioTrackId.Should().Be(playable.Id);
        decision.VideoStrategy.Should().Be("DirectStream");
    }

    [Fact]
    public async Task Under_Direct_the_version_whose_video_plays_untouched_wins_over_re_encoding_the_larger_one()
    {
        var smaller = Version("1080p", "h264", "mkv", Audio(1, "eac3", 6, isDefault: true));
        var film = Film(Version("2160p", "hevc", "mkv", Audio(1, "eac3", 6, isDefault: true)), smaller);
        var decision = await BrowserAsync(film, Direct, detected: 2, audioCodecs: ["aac"]);

        decision.SelectedMediaPartId.Should().Be(smaller.Id);
        decision.VideoStrategy.Should().Be("DirectStream");
        decision.AudioStrategy.Should().Be("Transcode");
    }

    [Fact]
    public async Task An_older_client_that_cannot_switch_tracks_gets_a_track_other_than_the_default_repackaged()
    {
        var surround = Audio(2, "ac3", 6);
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = Direct });
        var device = new ClientDevice
        {
            DeviceType = "AndroidTV", MaxAudioChannels = 6, SupportedVideoCodecs = ["h264"], SupportedAudioCodecs = TvCodecs.ToList(), SupportedContainers = ["mkv", "hls"]
        };

        var decision = await new BestPathDecisionManager(settings).DetermineBestPathAsync(device, Film(Audio(1, "aac", 2, isDefault: true), surround), 0, "None",
            requestedSubtitleId: Guid.Empty, requestedMaxResolution: 2160);

        decision.Strategy.Should().Be(StreamStrategy.Remux);
        decision.SelectedAudioTrackId.Should().Be(surround.Id);
    }
}
