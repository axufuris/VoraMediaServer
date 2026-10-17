using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Devices;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class MeasuredConnectionTests
{
    private const string DeviceId = "web-1";

    private readonly IStreamRepository _repository = Substitute.For<IStreamRepository>();
    private readonly IBestPathDecisionManager _decisions = Substitute.For<IBestPathDecisionManager>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IHardwareCapabilityService _hardware = Substitute.For<IHardwareCapabilityService>();
    private readonly Guid _mediaId = Guid.NewGuid();
    private ServerSetting _server = new();
    private (int LimitKbps, string Source, DisplayHint? Display, bool GpuEncodesHdr) _asked;

    public MeasuredConnectionTests()
    {
        var stream = new MediaStreamInfoDto { Id = _mediaId, Parts = [new MediaPartStreamInfoDto { Id = Guid.NewGuid(), Resolution = "2160p" }] };
        _repository.ResolvePlayableMediaIdAsync(_mediaId, Arg.Any<Guid?>()).Returns(_mediaId);
        _repository.GetMediaStreamInfoAsync(_mediaId).Returns(stream);
        _repository.GetClientDeviceAsync(DeviceId).Returns(new ClientDevice { DeviceId = DeviceId, LastIpAddress = "192.168.1.20", Location = ClientAddress.LocalNetwork });
        _settings.GetSettingsAsync().Returns(_ => _server);
        _decisions.DetermineBestPathAsync(default!, default!, default, default!).ReturnsForAnyArgs(call =>
        {
            _asked = (call.ArgAt<int>(2), call.ArgAt<string>(3), call.ArgAt<DisplayHint?>(14), call.ArgAt<bool>(15));
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
        _hardware);

    private static DeviceCapsDto Caps(int measuredKbps = 0, int bitratePickKbps = 0, int displayWidth = 0, int displayHeight = 0) =>
        new(["hevc", "h264"], ["aac"], ["mp4"], 2, 10_000, bitratePickKbps, DisplayWidth: displayWidth, DisplayHeight: displayHeight, MeasuredBandwidthKbps: measuredKbps);

    private Task DecideAsync(DeviceCapsDto caps) =>
        Manager().PreviewDecisionAsync(_mediaId, DeviceId, Guid.NewGuid(), null, capabilities: caps, clientAddress: "192.168.1.20");

    [Theory]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task Client_preference_and_bitrate_keep_a_stream_within_the_measured_connection(StreamingProfile profile)
    {
        _server = new ServerSetting { StreamingProfile = profile };

        await DecideAsync(Caps(measuredKbps: 40_000));

        _asked.LimitKbps.Should().Be(32_000);
        _asked.Source.Should().Be(ConnectionLimit.Source);
    }

    [Fact]
    public async Task Direct_ignores_the_measured_connection()
    {
        _server = new ServerSetting { StreamingProfile = StreamingProfile.DirectStreamPreference };

        await DecideAsync(Caps(measuredKbps: 40_000));

        _asked.LimitKbps.Should().Be(0);
    }

    [Fact]
    public async Task A_lower_bitrate_pick_still_wins_over_the_measured_connection()
    {
        _server = new ServerSetting { StreamingProfile = StreamingProfile.ClientPreference };

        await DecideAsync(Caps(measuredKbps: 40_000, bitratePickKbps: 8_000));

        (_asked.LimitKbps, _asked.Source).Should().Be((8_000, "Client Limit"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300_000)]
    [InlineData(900_000)]
    public async Task An_unmeasured_or_very_fast_connection_sets_no_limit(int measuredKbps)
    {
        _server = new ServerSetting { StreamingProfile = StreamingProfile.BandwidthOptimized };

        await DecideAsync(Caps(measuredKbps: measuredKbps));

        _asked.LimitKbps.Should().Be(0);
    }

    [Fact]
    public async Task The_screen_travels_to_the_decision_as_a_hint()
    {
        await DecideAsync(Caps(displayWidth: 2560, displayHeight: 1440));

        _asked.Display.Should().Be(new DisplayHint(2560, 1440));
    }

    [Fact]
    public async Task The_decision_is_told_when_the_gpu_can_keep_hdr()
    {
        _hardware.ResolveAsync(HardwareAccelerationType.Auto, "Auto", Arg.Any<CancellationToken>())
            .Returns(new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia, EncodesHdr: true));

        await DecideAsync(Caps());

        _asked.GpuEncodesHdr.Should().BeTrue();
    }

    [Theory]
    [InlineData("no hdr encode")]
    [InlineData("hardware off")]
    [InlineData("hardware encoding off")]
    [InlineData("keeping hdr off")]
    public async Task The_decision_is_told_the_gpu_cannot_keep_hdr(string situation)
    {
        _server = new ServerSetting
        {
            UseHardwareAcceleration = situation != "hardware off",
            UseHardwareEncoding = situation != "hardware encoding off",
            PreserveHdrInTranscodes = situation != "keeping hdr off",
        };
        _hardware.ResolveAsync(HardwareAccelerationType.Auto, "Auto", Arg.Any<CancellationToken>())
            .Returns(new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia, EncodesHdr: situation != "no hdr encode"));

        await DecideAsync(Caps());

        _asked.GpuEncodesHdr.Should().BeFalse();
    }
}
