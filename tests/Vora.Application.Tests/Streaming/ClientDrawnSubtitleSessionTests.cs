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
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class ClientDrawnSubtitleSessionTests
{
    private const string DeviceId = "living-room";

    private readonly IStreamRepository _repository = Substitute.For<IStreamRepository>();
    private readonly Guid _mediaId = Guid.NewGuid();
    private readonly SubtitleStreamInfoDto _pgs = new() { Id = Guid.NewGuid(), Codec = "hdmv_pgs_subtitle", Language = "eng", StreamIndex = 3, IsDefault = true };

    public ClientDrawnSubtitleSessionTests()
    {
        var media = new MediaStreamInfoDto { Id = _mediaId, Parts = [Part()] };
        _repository.ResolvePlayableMediaIdAsync(_mediaId, Arg.Any<Guid?>()).Returns(_mediaId);
        _repository.GetMediaStreamInfoAsync(_mediaId).Returns(media);
        _repository.GetClientDeviceAsync(DeviceId).Returns(_ => new ClientDevice { DeviceId = DeviceId, DeviceType = "AndroidTV", LastIpAddress = "192.168.1.30", Location = ClientAddress.LocalNetwork });
        _repository.CreateSessionAsync(Arg.Any<StreamSession>()).Returns(call => call.Arg<StreamSession>());
    }

    private MediaPartStreamInfoDto Part() => new()
    {
        Id = Guid.NewGuid(), Container = "mkv", Resolution = "1080p", OverallBitrate = 8_000_000,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "h264", IsDefault = true, Width = 1920, Height = 1080, StreamIndex = 0 }],
        AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "aac", Channels = 2, IsDefault = true, Language = "eng", StreamIndex = 1 }],
        SubtitleTracks = [new SubtitleStreamInfoDto { Id = Guid.NewGuid(), Codec = "subrip", Language = "eng", IsExternal = true }, new SubtitleStreamInfoDto { Id = Guid.NewGuid(), Codec = "subrip", Language = "fra", StreamIndex = 2 }, _pgs]
    };

    private StreamManager Manager()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = StreamingProfile.ClientPreference });
        return new StreamManager(
            _repository,
            new BestPathDecisionManager(settings),
            settings,
            Substitute.For<IStreamingTokenSigner>(),
            Substitute.For<IClientNotifier>(),
            Substitute.For<ITranscodeService>(),
            Options.Create(new StoragePathsOptions()),
            Substitute.For<IClientLocationResolver>(),
            Substitute.For<IMediaAnalyzerService>(),
            Substitute.For<IHardwareCapabilityService>());
    }

    private static DeviceCapsDto Caps(bool? drawsImageSubtitles) =>
        new(["h264", "hevc"], ["aac", "ac3"], ["mkv", "mp4"], 6, 0, 0, CanSwitchAudioTracks: true, DetectedAudioChannels: 6, RendersImageSubtitles: drawsImageSubtitles);

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_decision_and_the_stream_it_starts_agree_on_the_subtitle(bool? drawsImageSubtitles)
    {
        var preview = await Manager().PreviewDecisionAsync(_mediaId, DeviceId, Guid.NewGuid(), null, subtitleTrackId: _pgs.Id, capabilities: Caps(drawsImageSubtitles), clientAddress: "192.168.1.30");
        var started = await Manager().StartSessionAsync(_mediaId, DeviceId, Guid.NewGuid(), null, 0, subtitleTrackId: _pgs.Id, capabilities: Caps(drawsImageSubtitles), clientAddress: "192.168.1.30");

        started.Session.Strategy.Should().Be(preview.Strategy.ToString());
        started.Session.SubtitleStrategy.Should().Be(preview.SubtitleStrategy);
        started.Session.IsSubtitleBurnIn.Should().Be(preview.RequiresSubtitleBurnIn);
        started.Session.SubtitleTrackId.Should().Be(_pgs.Id);
        (started.SubtitleTrackPosition, started.AudioTrackPosition).Should().Be((preview.SelectedSubtitleTrackPosition, preview.SelectedAudioTrackPosition));

        if (drawsImageSubtitles == true)
        {
            (preview.Strategy, preview.RequiresSubtitleBurnIn, preview.SelectedSubtitleTrackPosition).Should().Be((StreamStrategy.DirectPlay, false, 1));
        }
        else
        {
            (preview.Strategy, preview.RequiresSubtitleBurnIn, preview.SelectedSubtitleTrackPosition).Should().Be((StreamStrategy.Transcode, true, (int?)null));
        }
    }

    [Fact]
    public async Task An_extra_is_decided_with_the_same_capability()
    {
        var extra = new MediaExtra { Id = Guid.NewGuid(), MediaItemId = _mediaId, Title = "Making of" };
        _repository.GetMediaExtraAsync(extra.Id).Returns(extra);
        _repository.GetExtraStreamInfoAsync(extra.Id).Returns(new MediaStreamInfoDto { Id = extra.Id, Parts = [Part()] });

        var drawn = await Manager().StartExtraSessionAsync(extra.Id, DeviceId, Guid.NewGuid(), null, 0, Caps(true), "192.168.1.30");
        var burned = await Manager().StartExtraSessionAsync(extra.Id, DeviceId, Guid.NewGuid(), null, 0, Caps(null), "192.168.1.30");

        (drawn.Session.Strategy, drawn.Session.IsSubtitleBurnIn).Should().Be(("DirectPlay", false));
        (burned.Session.Strategy, burned.Session.IsSubtitleBurnIn).Should().Be(("Transcode", true));
    }
}
