using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class ScreenHintDecisionTests
{
    private static readonly DisplayHint Screen1440 = new(2560, 1440);
    private static readonly DisplayHint Screen1080 = new(1920, 1080);

    private static ClientDevice Edge(bool hdrDisplay) => new()
    {
        DeviceType = "Browser",
        MaxAudioChannels = 2,
        MaxVideoBitDepth = 10,
        SupportedVideoCodecs = ["hevc", "h264"],
        SupportedAudioCodecs = ["aac"],
        SupportedContainers = ["mp4"],
        SupportedHdrFormats = hdrDisplay ? ["HDR10", "HLG"] : [],
    };

    private static ClientDevice Firefox() => new()
    {
        DeviceType = "Browser",
        MaxAudioChannels = 2,
        MaxVideoBitDepth = 10,
        SupportedVideoCodecs = ["h264", "vp9", "av1"],
        SupportedAudioCodecs = ["aac"],
        SupportedContainers = ["mp4", "webm"],
        SupportedHdrFormats = [],
    };

    private static MediaPartStreamInfoDto Uhd(string hdr = "HDR10", string audio = "dts", int channels = 6, long kbps = 13_667) => new()
    {
        Id = Guid.NewGuid(),
        Container = "mkv",
        Resolution = "2160p",
        OverallBitrate = kbps * 1000,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "hevc", IsDefault = true, Width = 3832, Height = 1640, FrameRate = 23.976, Bitrate = (kbps - 1_000) * 1000, BitDepth = 10, HdrType = hdr }],
        AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = audio, Channels = channels, IsDefault = true, Language = "eng", StreamIndex = 1 }],
    };

    private static MediaPartStreamInfoDto Hd() => new()
    {
        Id = Guid.NewGuid(),
        Container = "mp4",
        Resolution = "1080p",
        OverallBitrate = 10_000_000,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "h264", IsDefault = true, Width = 1920, Height = 800, FrameRate = 23.976, Bitrate = 9_500_000, BitDepth = 8, HdrType = "SDR" }],
        AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "aac", Channels = 2, IsDefault = true, Language = "eng", StreamIndex = 1 }],
    };

    private static MediaStreamInfoDto Film(params MediaPartStreamInfoDto[] parts) => new() { Id = Guid.NewGuid(), Parts = [.. parts] };

    private static Task<StreamDecisionDto> DecideAsync(
        ClientDevice client,
        MediaStreamInfoDto film,
        DisplayHint? display,
        StreamingProfile profile = StreamingProfile.ClientPreference,
        bool gpuEncodesHdr = false,
        int limitKbps = 0,
        int maxHeight = 0,
        Action<ServerSetting>? configure = null,
        Guid? subtitle = null)
    {
        var settings = new ServerSetting { StreamingProfile = profile, HdrTranscodeDownscale = "Never" };
        configure?.Invoke(settings);
        var repository = Substitute.For<ISystemSettingsRepository>();
        repository.GetSettingsAsync().Returns(settings);
        var hls = client.SupportedVideoCodecs.Contains("hevc") ? new[] { "h264", "hevc" } : new[] { "h264" };
        return new BestPathDecisionManager(repository).DetermineBestPathAsync(
            client, film, limitKbps, limitKbps > 0 ? "Client Limit" : "None",
            requestedSubtitleId: subtitle,
            requestedMaxResolution: maxHeight, hlsVideoCodecs: hls, display: display, gpuEncodesHdr: gpuEncodesHdr);
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task A_4k_hdr_film_on_a_smaller_hdr_screen_plays_the_original_video(StreamingProfile profile)
    {
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd()), Screen1440, profile);

        decision.VideoStrategy.Should().Be("DirectStream");
        decision.OutputHdrType.Should().Be("HDR10");
        decision.AudioStrategy.Should().Be("Transcode");
        decision.Reason.Should().NotContain("exceeds");
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task A_transcode_that_has_to_happen_is_fitted_to_the_screen(StreamingProfile profile)
    {
        var decision = await DecideAsync(Edge(hdrDisplay: false), Film(Uhd()), Screen1440, profile);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.OutputHdrType.Should().Be("SDR");
        (decision.OutputWidth, decision.OutputHeight).Should().Be((2560, 1096));
    }

    [Fact]
    public async Task Without_a_screen_a_transcode_keeps_the_film_at_its_own_size()
    {
        var decision = await DecideAsync(Edge(hdrDisplay: false), Film(Uhd()), display: null);

        (decision.OutputWidth, decision.OutputHeight).Should().Be((3832, 1640));
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task A_browser_that_cannot_play_the_4k_file_plays_the_1080p_file_rather_than_a_4k_transcode_on_a_1080p_screen(StreamingProfile profile)
    {
        var hd = Hd();
        var decision = await DecideAsync(Firefox(), Film(Uhd(audio: "aac", channels: 2), hd), Screen1080, profile);

        decision.SelectedMediaPartId.Should().Be(hd.Id);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task The_largest_version_that_plays_still_wins_on_a_smaller_screen(StreamingProfile profile)
    {
        var uhd = Uhd(audio: "aac", channels: 2);
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(uhd, Hd()), Screen1080, profile);

        decision.SelectedMediaPartId.Should().Be(uhd.Id);
        decision.VideoStrategy.Should().NotBe("Transcode");
        var rival = decision.EvaluatedOptions.Where(o => o.MediaPartId != uhd.Id).Min(o => o.PenaltyScore);
        (rival - decision.EvaluatedOptions.Where(o => o.MediaPartId == uhd.Id).Min(o => o.PenaltyScore)).Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task A_resolution_picked_by_the_viewer_is_still_a_limit()
    {
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd()), Screen1440, maxHeight: 1080);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.OutputHeight.Should().BeLessThanOrEqualTo(1080);
        decision.Reason.Should().Contain("exceeds what the client plays (2048x1080)");
    }

    [Fact]
    public async Task A_transcode_for_bitrate_keeps_hdr_as_10_bit_hevc_when_the_gpu_and_the_player_can()
    {
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd(kbps: 60_000)), Screen1440, gpuEncodesHdr: true, limitKbps: 20_000);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.TargetVideoCodec.Should().Be("hevc");
        decision.OutputHdrType.Should().Be("HDR10");
        decision.Reason.Should().Contain("Keeping HDR10 by encoding 10-bit HEVC.");
        decision.BandwidthKbps.Should().BeLessThanOrEqualTo(20_000);
    }

    [Fact]
    public async Task An_hlg_film_keeps_hlg()
    {
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd(hdr: "HLG", kbps: 60_000)), Screen1440, gpuEncodesHdr: true, limitKbps: 20_000);

        (decision.TargetVideoCodec, decision.OutputHdrType).Should().Be(("hevc", "HLG"));
    }

    [Fact]
    public async Task Keeping_hdr_does_not_shrink_the_picture_the_way_tone_mapping_on_always_does()
    {
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd(kbps: 60_000)), Screen1440, gpuEncodesHdr: true, limitKbps: 20_000,
            configure: settings => settings.HdrTranscodeDownscale = "Always");

        decision.OutputHdrType.Should().Be("HDR10");
        decision.OutputWidth.Should().BeGreaterThan(1920);
    }

    [Theory]
    [InlineData("no GPU", "the server has no GPU that encodes 10-bit HEVC")]
    [InlineData("setting off", "keeping HDR in a transcode is turned off on the server")]
    [InlineData("hevc never", "HEVC encoding is set to Never on the server")]
    public async Task Hdr_is_tone_mapped_and_the_reason_says_why_when_it_cannot_be_kept(string situation, string why)
    {
        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd(kbps: 60_000)), Screen1440,
            gpuEncodesHdr: situation != "no GPU", limitKbps: 20_000,
            configure: settings =>
            {
                if (situation == "setting off") settings.PreserveHdrInTranscodes = false;
                if (situation == "hevc never") settings.EnableHevcEncoding = 0;
            });

        (decision.TargetVideoCodec, decision.OutputHdrType).Should().Be(("h264", "SDR"));
        decision.Reason.Should().Contain($"HDR10 is tone-mapped to SDR because {why}.");
    }

    [Fact]
    public async Task Hdr_is_tone_mapped_for_a_player_whose_stream_cannot_take_hevc()
    {
        var decision = await DecideAsync(Firefox(), Film(Uhd(kbps: 60_000)), Screen1440, gpuEncodesHdr: true, limitKbps: 20_000);

        decision.OutputHdrType.Should().Be("SDR");
    }

    [Fact]
    public async Task Hdr_is_tone_mapped_when_a_subtitle_is_burned_in()
    {
        var part = Uhd(kbps: 60_000);
        var pgs = Guid.NewGuid();
        part.SubtitleTracks.Add(new SubtitleStreamInfoDto { Id = pgs, Codec = "hdmv_pgs_subtitle", Language = "eng" });

        var decision = await DecideAsync(Edge(hdrDisplay: true), Film(part), Screen1440, gpuEncodesHdr: true, subtitle: pgs);

        decision.RequiresSubtitleBurnIn.Should().BeTrue();
        (decision.TargetVideoCodec, decision.OutputHdrType).Should().Be(("h264", "SDR"));
    }

    [Fact]
    public async Task Keeping_hdr_spends_the_bitrate_hevc_needs_rather_than_h264s()
    {
        var kept = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd(kbps: 30_000)), Screen1440, gpuEncodesHdr: true, limitKbps: 100_000, maxHeight: 1080);
        var toneMapped = await DecideAsync(Edge(hdrDisplay: true), Film(Uhd(kbps: 30_000)), Screen1440, gpuEncodesHdr: false, limitKbps: 100_000, maxHeight: 1080);

        kept.OutputHdrType.Should().Be("HDR10");
        kept.BandwidthKbps.Should().BeLessThan(toneMapped.BandwidthKbps);
    }
}
