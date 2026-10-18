using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Devices;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Streaming;

public class RemoteBandwidthLimitTests
{
    private const string DeviceId = "phone-1";
    private const string HotelAddress = "203.0.113.40";
    private const string HomeAddress = "192.168.1.20";

    private readonly IStreamRepository _repository = Substitute.For<IStreamRepository>();
    private readonly IBestPathDecisionManager _decisions = Substitute.For<IBestPathDecisionManager>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly Guid _mediaId = Guid.NewGuid();
    private readonly MediaExtra _extra;
    private ServerSetting _server = new();
    private List<NowPlayingSessionDto> _playing = [];
    private (int LimitKbps, string Source) _asked;

    public RemoteBandwidthLimitTests()
    {
        _extra = new MediaExtra { Id = Guid.NewGuid(), MediaItemId = _mediaId, Title = "Trailer" };
        var stream = new MediaStreamInfoDto { Id = _mediaId, Parts = [new MediaPartStreamInfoDto { Id = Guid.NewGuid(), Resolution = "1080p" }] };
        _repository.ResolvePlayableMediaIdAsync(_mediaId, Arg.Any<Guid?>()).Returns(_mediaId);
        _repository.GetMediaStreamInfoAsync(_mediaId).Returns(stream);
        _repository.GetMediaExtraAsync(_extra.Id).Returns(_extra);
        _repository.GetExtraStreamInfoAsync(_extra.Id).Returns(new MediaStreamInfoDto { Id = _extra.Id, Parts = [new MediaPartStreamInfoDto { Id = Guid.NewGuid(), Resolution = "1080p" }] });
        _repository.GetClientDeviceAsync(DeviceId).Returns(_ => new ClientDevice { DeviceId = DeviceId });
        _repository.GetNowPlayingSessionsAsync(Arg.Any<DateTime>()).Returns(_ => _playing);
        _repository.CreateSessionAsync(Arg.Any<StreamSession>()).Returns(call => call.Arg<StreamSession>());
        _settings.GetSettingsAsync().Returns(_ => _server);
        _decisions.DetermineBestPathAsync(Arg.Any<ClientDevice>(), Arg.Any<MediaStreamInfoDto>(), Arg.Any<int>(), Arg.Any<string>()).ReturnsForAnyArgs(call =>
        {
            _asked = (call.ArgAt<int>(2), call.ArgAt<string>(3));
            return new StreamDecisionDto();
        });
    }

    private StreamManager Manager() => new(
        _repository,
        _decisions,
        _settings,
        Substitute.For<IStreamingTokenSigner>(),
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITranscodeService>(),
        Options.Create(new StoragePathsOptions()),
        Substitute.For<IClientLocationResolver>(),
        Substitute.For<IMediaAnalyzerService>(),
        Substitute.For<IHardwareCapabilityService>());

    private static NowPlayingSessionDto RemoteStream(int kbps) => new() { DeviceId = "tv-" + Guid.NewGuid().ToString("N"), IpAddress = "198.51.100.7", BandwidthKbps = kbps };

    private Task PlayAsync(string address) =>
        Manager().PreviewDecisionAsync(_mediaId, DeviceId, Guid.NewGuid(), null, clientAddress: address);

    private Task PlayExtraAsync(string address) =>
        Manager().StartExtraSessionAsync(_extra.Id, DeviceId, Guid.NewGuid(), null, 0, clientAddress: address);

    [Fact]
    public async Task A_used_up_upload_still_limits_a_new_remote_stream_to_the_floor()
    {
        _server = new ServerSetting { InternetUploadSpeedMbps = 20 };
        _playing = [RemoteStream(12_000), RemoteStream(9_000)];

        await PlayAsync(HotelAddress);

        _asked.Should().Be((StreamManager.UploadFloorKbps, "Server Upload Limit"));
    }

    [Fact]
    public async Task An_upload_with_room_left_limits_a_new_remote_stream_to_that_room()
    {
        _server = new ServerSetting { InternetUploadSpeedMbps = 20 };
        _playing = [RemoteStream(12_000)];

        await PlayAsync(HotelAddress);

        _asked.Should().Be((8_000, "Server Upload Limit"));
    }

    [Fact]
    public async Task An_upload_smaller_than_the_floor_is_never_exceeded()
    {
        _server = new ServerSetting { InternetUploadSpeedMbps = 1 };
        _playing = [RemoteStream(900)];

        await PlayAsync(HotelAddress);

        _asked.Should().Be((1_000, "Server Upload Limit"));
    }

    [Fact]
    public async Task A_used_up_upload_still_wins_over_a_higher_remote_limit()
    {
        _server = new ServerSetting { InternetUploadSpeedMbps = 20, MaxRemoteStreamBitrateMbps = 10 };
        _playing = [RemoteStream(20_000)];

        await PlayAsync(HotelAddress);

        _asked.Should().Be((StreamManager.UploadFloorKbps, "Server Upload Limit"));
    }

    [Fact]
    public async Task An_extra_played_away_from_home_keeps_to_the_remote_limit()
    {
        _server = new ServerSetting { MaxRemoteStreamBitrateMbps = 8 };

        await PlayExtraAsync(HotelAddress);

        _asked.Should().Be((8_000, "Server Limit"));
    }

    [Fact]
    public async Task An_extra_played_away_from_home_keeps_to_the_upload_left()
    {
        _server = new ServerSetting { InternetUploadSpeedMbps = 20 };
        _playing = [RemoteStream(15_000)];

        await PlayExtraAsync(HotelAddress);

        _asked.Should().Be((5_000, "Server Upload Limit"));
    }

    [Fact]
    public async Task An_extra_played_at_home_has_no_server_limit()
    {
        _server = new ServerSetting { MaxRemoteStreamBitrateMbps = 8, InternetUploadSpeedMbps = 20 };
        _playing = [RemoteStream(20_000)];

        await PlayExtraAsync(HomeAddress);

        _asked.Should().Be((0, "None"));
    }
}
