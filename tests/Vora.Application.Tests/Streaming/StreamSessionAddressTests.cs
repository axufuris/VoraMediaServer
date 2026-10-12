using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Devices;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Streaming;

public class StreamSessionAddressTests
{
    private const string DeviceId = "web-1";
    private const string HomeAddress = "192.168.1.20";
    private const string HotelAddress = "203.0.113.9";

    private readonly IStreamRepository _repository = Substitute.For<IStreamRepository>();
    private readonly IBestPathDecisionManager _decisions = Substitute.For<IBestPathDecisionManager>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IClientLocationResolver _locations = Substitute.For<IClientLocationResolver>();
    private readonly Guid _mediaId = Guid.NewGuid();
    private readonly Guid _extraId = Guid.NewGuid();
    private readonly ClientDevice _device = new() { DeviceId = DeviceId, LastIpAddress = HomeAddress, Location = ClientAddress.LocalNetwork };

    public StreamSessionAddressTests()
    {
        var stream = new MediaStreamInfoDto { Id = _mediaId, Parts = [new MediaPartStreamInfoDto { Id = Guid.NewGuid(), Resolution = "1080p" }] };
        _repository.ResolvePlayableMediaIdAsync(_mediaId, Arg.Any<Guid?>()).Returns(_mediaId);
        _repository.GetMediaStreamInfoAsync(_mediaId).Returns(stream);
        _repository.GetClientDeviceAsync(DeviceId).Returns(_device);
        _repository.CreateSessionAsync(Arg.Any<StreamSession>()).Returns(call => call.Arg<StreamSession>());
        _repository.GetNowPlayingSessionsAsync(Arg.Any<DateTime>()).Returns([]);
        _repository.GetMediaExtraAsync(_extraId).Returns(new Vora.Domain.Entities.Media.MediaExtra { Id = _extraId, MediaItemId = _mediaId });
        _repository.GetExtraStreamInfoAsync(_extraId).Returns(stream);
        _settings.GetSettingsAsync().Returns(new ServerSetting { MaxRemoteStreamBitrateMbps = 8 });
        _decisions.DetermineBestPathAsync(default!, default!, default, default!).ReturnsForAnyArgs(new StreamDecisionDto());
        _locations.ResolveAsync(HotelAddress, Arg.Any<string?>()).Returns("Denver, CO, US");
    }

    private StreamManager Manager() => new(
        _repository,
        _decisions,
        _settings,
        Substitute.For<IStreamingTokenSigner>(),
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITranscodeService>(),
        Options.Create(new StoragePathsOptions()),
        _locations);

    private Task<(StreamSession Session, string StreamUrl)> StartAsync(string? clientAddress) =>
        Manager().StartSessionAsync(_mediaId, DeviceId, Guid.NewGuid(), Guid.NewGuid(), 0, clientAddress: clientAddress);

    [Fact]
    public async Task A_stream_records_the_address_it_was_started_from()
    {
        var (session, _) = await StartAsync(HotelAddress);

        session.IpAddress.Should().Be(HotelAddress);
        session.Location.Should().Be("Denver, CO, US");
    }

    [Fact]
    public async Task The_devices_known_location_is_reused_when_the_address_has_not_changed()
    {
        var (session, _) = await StartAsync(HomeAddress);

        session.IpAddress.Should().Be(HomeAddress);
        session.Location.Should().Be(ClientAddress.LocalNetwork);
        await _locations.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task A_device_that_was_never_located_is_looked_up()
    {
        _device.LastIpAddress = HotelAddress;
        _device.Location = ClientAddress.UnknownLocation;

        var (session, _) = await StartAsync(HotelAddress);

        session.Location.Should().Be("Denver, CO, US");
    }

    [Fact]
    public async Task Without_an_address_from_the_request_the_devices_last_address_is_recorded()
    {
        var (session, _) = await StartAsync(null);

        session.IpAddress.Should().Be(HomeAddress);
        session.Location.Should().Be(ClientAddress.LocalNetwork);
    }

    [Fact]
    public async Task A_trailer_records_its_address_too()
    {
        var (session, _) = await Manager().StartExtraSessionAsync(_extraId, DeviceId, Guid.NewGuid(), Guid.NewGuid(), 0, clientAddress: HotelAddress);

        session.IpAddress.Should().Be(HotelAddress);
        session.Location.Should().Be("Denver, CO, US");
    }

    [Fact]
    public async Task A_device_last_seen_at_home_is_treated_as_remote_when_this_request_is_not()
    {
        await StartAsync(HotelAddress);

        await _decisions.Received(1).DetermineBestPathAsync(
            _device, Arg.Any<MediaStreamInfoDto>(), Arg.Is(8000), "Server Limit",
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task A_device_last_seen_away_is_treated_as_local_when_this_request_is()
    {
        _device.LastIpAddress = HotelAddress;
        _device.Location = "Denver, CO, US";

        await StartAsync(HomeAddress);

        await _decisions.Received(1).DetermineBestPathAsync(
            _device, Arg.Any<MediaStreamInfoDto>(), Arg.Is(0), "None",
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>());
    }
}
