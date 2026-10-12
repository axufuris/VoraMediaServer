using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class CoverImageTrackDecisionTests
{
    private static readonly Guid FilmTrackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CoverTrackId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClientDevice Browser() => new()
    {
        DeviceType = "Web",
        MaxAudioChannels = 6,
        MaxVideoBitDepth = 8,
        SupportedVideoCodecs = new List<string> { "h264" },
        SupportedAudioCodecs = new List<string> { "aac", "eac3" },
        SupportedContainers = new List<string> { "mkv", "mp4" }
    };

    private static MediaStreamInfoDto Episode(params TrackStreamInfoDto[] videoTracks) => new()
    {
        Id = Guid.NewGuid(),
        Parts =
        [
            new MediaPartStreamInfoDto
            {
                Id = Guid.NewGuid(),
                Resolution = "720p",
                Container = "mkv",
                OverallBitrate = 3_000_000,
                VideoTracks = videoTracks.ToList(),
                AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "eac3", Channels = 6, IsDefault = true, Language = "eng" }]
            }
        ]
    };

    private static Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto item)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = StreamingProfile.ClientPreference });
        return new BestPathDecisionManager(settings).DetermineBestPathAsync(
            Browser(), item, maxAllowedBandwidthKbps: 0, bandwidthLimitSource: "test", requestedMaxResolution: 1080);
    }

    [Fact]
    public async Task The_film_is_played_when_the_cover_image_is_listed_first()
    {
        var item = Episode(
            new TrackStreamInfoDto { Id = CoverTrackId, Codec = "mjpeg", BitDepth = 8 },
            new TrackStreamInfoDto { Id = FilmTrackId, Codec = "h264", BitDepth = 8 });

        var decision = await DecideAsync(item);

        decision.SelectedVideoTrackId.Should().Be(FilmTrackId);
    }

    [Fact]
    public async Task The_film_is_played_when_the_cover_image_carries_the_default_flag()
    {
        var item = Episode(
            new TrackStreamInfoDto { Id = FilmTrackId, Codec = "h264", BitDepth = 8 },
            new TrackStreamInfoDto { Id = CoverTrackId, Codec = "png", BitDepth = 8, IsDefault = true });

        var decision = await DecideAsync(item);

        decision.SelectedVideoTrackId.Should().Be(FilmTrackId);
    }

    [Fact]
    public async Task A_file_whose_only_video_is_motion_jpeg_still_plays()
    {
        var item = Episode(new TrackStreamInfoDto { Id = FilmTrackId, Codec = "mjpeg", BitDepth = 8, IsDefault = true });

        var decision = await DecideAsync(item);

        decision.SelectedVideoTrackId.Should().Be(FilmTrackId);
    }
}
