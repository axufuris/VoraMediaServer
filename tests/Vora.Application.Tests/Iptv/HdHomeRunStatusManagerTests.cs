using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class HdHomeRunStatusManagerTests
{
    private readonly IIptvRepository _repository = Substitute.For<IIptvRepository>();
    private readonly IHdHomeRunClient _client = Substitute.For<IHdHomeRunClient>();
    private readonly TunerRegistry _tuners = new();

    private HdHomeRunStatusManager Manager() => new(_repository, _client, _tuners);

    private IptvPlaylist Tuner()
    {
        var playlist = new IptvPlaylist { Name = "Antenna", SourceType = IptvSourceType.HdHomeRun, ServerUrl = HdHomeRunFixtures.Address };
        _repository.GetPlaylistByIdAsync(playlist.Id).Returns(playlist);
        return playlist;
    }

    [Fact]
    public async Task The_status_names_the_device_counts_its_tuners_and_adds_what_vora_is_using()
    {
        var playlist = Tuner();
        _tuners.TryAcquire(playlist.Id, 0, "timeshift:a", TunerLeaseKind.Timeshift);
        _tuners.TryAcquire(playlist.Id, 0, "dvr:b", TunerLeaseKind.Dvr);
        _client.GetStatusAsync(playlist, Arg.Any<CancellationToken>()).Returns(new HdHomeRunStatus(
            new HdHomeRunDevice("HDHomeRun FLEX 4K", "HDHR5-4K", "1080ABCD", 4, "20240101"),
            [
                new HdHomeRunTuner(0, true, "7.1", "WABC-DT", 473_000_000, 84, 100, 98, 19_393_088, "192.168.1.20"),
                new HdHomeRunTuner(1, false, null, null, null, null, null, null, null, null)
            ]));

        var status = await Manager().GetAsync(playlist.Id, TestContext.Current.CancellationToken);

        status.Should().NotBeNull();
        status!.Succeeded.Should().BeTrue();
        status.DeviceName.Should().Be("HDHomeRun FLEX 4K");
        status.Model.Should().Be("HDHR5-4K");
        status.FirmwareVersion.Should().Be("20240101");
        status.TunerCount.Should().Be(4);
        status.TunersInUse.Should().Be(1);
        status.VoraStreams.Should().Be(2);
        status.Tuners[0].FrequencyMhz.Should().Be(473);
        status.Tuners[0].ChannelName.Should().Be("WABC-DT");
        status.Tuners[0].TargetAddress.Should().Be("192.168.1.20");
        status.Tuners[1].InUse.Should().BeFalse();
    }

    [Fact]
    public async Task A_device_that_gave_no_details_is_named_by_the_playlist_and_counted_by_its_tuners()
    {
        var playlist = Tuner();
        _client.GetStatusAsync(playlist, Arg.Any<CancellationToken>()).Returns(new HdHomeRunStatus(null, [new HdHomeRunTuner(0, false, null, null, null, null, null, null, null, null)]));

        var status = await Manager().GetAsync(playlist.Id, TestContext.Current.CancellationToken);

        status!.DeviceName.Should().Be("Antenna");
        status.TunerCount.Should().Be(1);
    }

    [Fact]
    public async Task A_device_that_cannot_be_reached_is_an_answer_with_the_reason_not_an_error()
    {
        var playlist = Tuner();
        _tuners.TryAcquire(playlist.Id, 0, "timeshift:a", TunerLeaseKind.Timeshift);
        _client.GetStatusAsync(playlist, Arg.Any<CancellationToken>()).Returns<HdHomeRunStatus>(_ => throw new IptvSourceException("The HDHomeRun did not answer in time."));

        var status = await Manager().GetAsync(playlist.Id, TestContext.Current.CancellationToken);

        status!.Succeeded.Should().BeFalse();
        status.Error.Should().Be("The HDHomeRun did not answer in time.");
        status.VoraStreams.Should().Be(1);
        status.Tuners.Should().BeEmpty();
    }

    [Fact]
    public async Task A_playlist_that_is_not_a_tuner_or_does_not_exist_has_no_status()
    {
        var m3u = new IptvPlaylist { Name = "List", SourceType = IptvSourceType.M3u, M3uUrl = "https://example.com/list.m3u" };
        _repository.GetPlaylistByIdAsync(m3u.Id).Returns(m3u);

        (await Manager().GetAsync(m3u.Id, TestContext.Current.CancellationToken)).Should().BeNull();
        (await Manager().GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeNull();
        await _client.DidNotReceiveWithAnyArgs().GetStatusAsync(default!, TestContext.Current.CancellationToken);
    }
}
