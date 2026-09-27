using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

// The scorer compares "what would the viewer actually receive" across parts.
// That only works if both sides of the comparison are heights the encoder can
// emit: a phone reporting a 1344p ceiling is served 1080p, so an option that
// downscales 4K to 1344 has to be scored as the 1080p it really is. Scored
// raw, it looked like a perfect fit and beat a 1080p file that direct-plays —
// a GPU transcode to deliver exactly what was already sitting on disk.
public class ResolutionFitDecisionTests
{
    private static readonly Guid FourKPartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FullHdPartId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid QuadHdPartId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly ISystemSettingsRepository _settingsRepo = Substitute.For<ISystemSettingsRepository>();

    private BestPathDecisionManager Build(string hdrDownscale = "Never")
    {
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting
        {
            StreamingProfile = StreamingProfile.ClientPreference,
            HdrTranscodeDownscale = hdrDownscale
        });
        return new BestPathDecisionManager(_settingsRepo);
    }

    // A Pixel-class phone: HEVC-capable, HLS, and a 1344-pixel short edge that
    // is a real decoder/display limit rather than a delivery rung.
    private static ClientDevice Phone() => new()
    {
        DeviceType = "Android",
        MaxAudioChannels = 6,
        MaxVideoBitDepth = 10,
        SupportedVideoCodecs = new List<string> { "hevc", "h264" },
        SupportedAudioCodecs = new List<string> { "aac", "eac3", "ac3" },
        SupportedContainers = new List<string> { "mkv", "mp4" },
        SupportedHdrFormats = new List<string> { "DolbyVision", "HDR10", "HDR10Plus" }
    };

    private static MediaPartStreamInfoDto Part(Guid id, string resolution, string? hdrType = null, long bitrate = 20_000_000) => new()
    {
        Id = id,
        Resolution = resolution,
        Container = "mkv",
        OverallBitrate = bitrate,
        VideoTracks = new List<TrackStreamInfoDto>
        {
            new() { Id = Guid.NewGuid(), Codec = "hevc", IsDefault = true, HdrType = hdrType, BitDepth = hdrType == null ? 8 : 10 }
        },
        AudioTracks = new List<TrackStreamInfoDto>
        {
            new() { Id = Guid.NewGuid(), Codec = "eac3", Channels = 6, IsDefault = true, Language = "eng" }
        }
    };

    private static MediaStreamInfoDto Item(params MediaPartStreamInfoDto[] parts) =>
        new() { Id = Guid.NewGuid(), Parts = parts.ToList() };

    private Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto item, int maxResolution, ClientDevice? client = null, string hdrDownscale = "Never", Guid? requestedVideoId = null) =>
        Build(hdrDownscale).DetermineBestPathAsync(
            client ?? Phone(),
            item,
            maxAllowedBandwidthKbps: 0,
            bandwidthLimitSource: "test",
            requestedVideoId: requestedVideoId,
            requestedMaxResolution: maxResolution);

    // The reported case, exactly: both versions present, ceiling between rungs.
    [Fact]
    public async Task A_phone_ceiling_between_rungs_takes_the_1080p_file_over_a_4k_transcode()
    {
        var item = Item(Part(FourKPartId, "2160p", hdrType: "DoVi/HDR10Plus"), Part(FullHdPartId, "1080p"));

        var decision = await DecideAsync(item, maxResolution: 1344);

        decision.SelectedMediaPartId.Should().Be(FullHdPartId);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
        decision.VideoStrategy.Should().NotBe("Transcode");
    }

    // The fix must not cost 4K clients their 4K.
    [Fact]
    public async Task A_client_that_can_take_4k_still_gets_the_4k_file()
    {
        var item = Item(Part(FourKPartId, "2160p", hdrType: "DoVi/HDR10Plus"), Part(FullHdPartId, "1080p"));

        var decision = await DecideAsync(item, maxResolution: 2160);

        decision.SelectedMediaPartId.Should().Be(FourKPartId);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    // Nothing else to pick: the transcode still happens, but the resolution it
    // declares is now the one the encoder will really emit.
    [Fact]
    public async Task A_4k_only_item_still_transcodes_and_declares_the_rung_it_emits()
    {
        var decision = await DecideAsync(Item(Part(FourKPartId, "2160p")), maxResolution: 1344);

        decision.SelectedMediaPartId.Should().Be(FourKPartId);
        decision.VideoStrategy.Should().Be("Transcode");
        decision.OutputResolution.Should().Be("1080p");
    }

    [Fact]
    public async Task The_higher_source_still_wins_when_the_client_can_take_it()
    {
        var item = Item(Part(QuadHdPartId, "1440p"), Part(FourKPartId, "2160p"));

        var decision = await DecideAsync(item, maxResolution: 2160);

        decision.SelectedMediaPartId.Should().Be(FourKPartId);
    }

    // The HDR downscale rule clamps the 4K option to 1080p, so both options
    // deliver 1080p and the one needing no downscale work wins.
    [Fact]
    public async Task With_hdr_downscale_always_the_sdr_1080p_part_still_wins()
    {
        var item = Item(Part(FourKPartId, "2160p", hdrType: "HDR10"), Part(FullHdPartId, "1080p"));

        var decision = await DecideAsync(item, maxResolution: 2160, hdrDownscale: "Always");

        decision.SelectedMediaPartId.Should().Be(FullHdPartId);
    }

    // Quality & Tracks hands back a track id. That must still pin the choice,
    // however badly the scorer would rate it.
    [Fact]
    public async Task Asking_for_the_4k_track_by_id_still_gets_it()
    {
        var fourK = Part(FourKPartId, "2160p", hdrType: "DoVi/HDR10Plus");
        var item = Item(fourK, Part(FullHdPartId, "1080p"));

        var decision = await DecideAsync(item, maxResolution: 1344, requestedVideoId: fourK.VideoTracks[0].Id);

        decision.SelectedMediaPartId.Should().Be(FourKPartId);
        decision.VideoStrategy.Should().Be("Transcode");
    }

    // The admin session log is read by a human chasing exactly this class of
    // report, so it quotes the device's real limit, not the snapped one.
    [Fact]
    public async Task The_reason_still_quotes_the_raw_client_maximum()
    {
        var decision = await DecideAsync(Item(Part(FourKPartId, "2160p")), maxResolution: 1344);

        decision.Reason.Should().Contain("exceeds client maximum (1344p)");
    }

    [Fact]
    public async Task The_option_log_shows_the_ceiling_the_snap_and_the_output()
    {
        var decision = await DecideAsync(Item(Part(FourKPartId, "2160p")), maxResolution: 1344);

        var logged = decision.EvaluatedOptions.Should().ContainSingle().Subject;
        logged.ClientMaxHeight.Should().Be(1344);
        logged.DeliverableHeight.Should().Be(1080);
        logged.OutputHeight.Should().Be(1080);
    }

    // A ceiling already on the ladder must pass through untouched, or the snap
    // would quietly cost every well-behaved client a rung.
    [Theory]
    [InlineData(2160, 2160)]
    [InlineData(1440, 1440)]
    [InlineData(1080, 1080)]
    [InlineData(720, 720)]
    [InlineData(1344, 1080)]
    [InlineData(1600, 1440)]
    [InlineData(900, 720)]
    public async Task A_ceiling_is_reported_snapped_down_to_the_rung_below_it(int requested, int expected)
    {
        var decision = await DecideAsync(Item(Part(FourKPartId, "2160p")), maxResolution: requested);

        decision.EvaluatedOptions[0].DeliverableHeight.Should().Be(expected);
    }

    // No ceiling reported means the 2160 default, which is already a rung.
    [Fact]
    public async Task An_unreported_ceiling_still_means_4k()
    {
        var item = Item(Part(FourKPartId, "2160p"), Part(FullHdPartId, "1080p"));

        var decision = await DecideAsync(item, maxResolution: 0);

        decision.SelectedMediaPartId.Should().Be(FourKPartId);
    }
}
