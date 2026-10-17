using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class TranscodeSizeDecisionTests
{
    private readonly ISystemSettingsRepository _settingsRepo = Substitute.For<ISystemSettingsRepository>();

    private static ClientDevice Browser() => new()
    {
        DeviceType = "Browser",
        MaxAudioChannels = 6,
        MaxVideoBitDepth = 8,
        SupportedVideoCodecs = ["h264"],
        SupportedAudioCodecs = ["aac", "ac3"],
        SupportedContainers = ["mp4", "mkv"],
        SupportedHdrFormats = [],
    };

    private static MediaStreamInfoDto Film(string? resolution, int? width, int? height, long overallBitrate = 6_385_000, double? frameRate = 23.976) => new()
    {
        Id = Guid.NewGuid(),
        Parts =
        [
            new MediaPartStreamInfoDto
            {
                Id = Guid.NewGuid(),
                FilePath = "/movies/film.mkv",
                Resolution = resolution,
                Container = "mkv",
                OverallBitrate = overallBitrate,
                VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "hevc", IsDefault = true, BitDepth = 10, Width = width, Height = height, FrameRate = frameRate }],
                AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "aac", Channels = 6, IsDefault = true, Language = "eng", StreamIndex = 1 }],
            },
        ],
    };

    private Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto film, int maxWidth, int maxHeight, int bandwidthKbps = 0)
    {
        _settingsRepo.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = StreamingProfile.ClientPreference });
        return new BestPathDecisionManager(_settingsRepo).DetermineBestPathAsync(
            Browser(),
            film,
            maxAllowedBandwidthKbps: bandwidthKbps,
            bandwidthLimitSource: "Client Limit",
            requestedMaxResolution: maxHeight,
            requestedMaxWidth: maxWidth);
    }

    // Issue #145's film on a 2560x1440 screen: it used to be scaled to 1440
    // lines tall, 3590 wide, and encoded at the source's own 6.4 Mbps.
    [Fact]
    public async Task A_wide_film_is_fitted_to_the_screen_by_its_width()
    {
        var decision = await DecideAsync(Film("2160p", 3840, 1540), maxWidth: 2560, maxHeight: 1440);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.OutputWidth.Should().Be(2560);
        decision.OutputHeight.Should().Be(1026);
        decision.OutputResolution.Should().Be("1440p");
        decision.BandwidthKbps.Should().BeCloseTo(8175, 5);
        decision.Quality.Should().Be("Transcode (6.9 Mbps)");
    }

    [Fact]
    public async Task An_ultrawide_screen_gets_the_film_at_its_own_width()
    {
        var decision = await DecideAsync(Film("2160p", 3840, 1540), maxWidth: 3440, maxHeight: 1440);

        decision.OutputWidth.Should().Be(3440);
        decision.OutputHeight.Should().Be(1380);
    }

    [Fact]
    public async Task A_4k_file_with_no_resolution_in_its_name_is_still_fitted_to_the_screen()
    {
        var decision = await DecideAsync(Film(resolution: null, 3840, 2160), maxWidth: 1920, maxHeight: 1080);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.Reason.Should().Contain("exceeds the client's screen (1920x1080)");
        decision.OutputWidth.Should().Be(1920);
        decision.OutputHeight.Should().Be(1080);
    }

    [Fact]
    public async Task A_slow_connection_narrows_the_picture_and_leaves_headroom_under_its_limit()
    {
        var decision = await DecideAsync(Film("2160p", 3840, 2160, overallBitrate: 25_000_000), maxWidth: 2560, maxHeight: 1440, bandwidthKbps: 4000);

        decision.OutputWidth.Should().Be(1280);
        decision.OutputHeight.Should().Be(720);
        decision.BandwidthKbps.Should().Be(4000);
        decision.Quality.Should().Be("Transcode (3.4 Mbps)");
    }

    [Fact]
    public async Task A_file_of_unknown_size_is_converted_at_its_own_size_rather_than_480p()
    {
        var decision = await DecideAsync(Film(resolution: null, width: null, height: null), maxWidth: 2560, maxHeight: 1440);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.OutputWidth.Should().Be(0);
        decision.OutputHeight.Should().Be(0);
    }

    [Fact]
    public async Task A_file_that_fits_and_plays_is_not_touched()
    {
        var film = Film("1080p", 1920, 800);
        film.Parts[0].VideoTracks[0].Codec = "h264";
        film.Parts[0].VideoTracks[0].BitDepth = 8;
        film.Parts[0].Container = "mp4";

        var decision = await DecideAsync(film, maxWidth: 2560, maxHeight: 1440);

        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
        decision.OutputWidth.Should().Be(0);
        decision.OutputHeight.Should().Be(0);
    }
}
