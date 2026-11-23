using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

[Collection(EpgServiceCollection.Name)]
public sealed class HdHomeRunGuideTests : IDisposable
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private readonly string _cacheFolder = Path.Combine(Path.GetTempPath(), "vora-hdhr-guide-" + Guid.NewGuid().ToString("N"));
    private readonly IIptvRepository _repository = Substitute.For<IIptvRepository>();
    private readonly StubUpstream _upstream = new();
    private readonly ManualClock _clock = new();
    private readonly DateTime _start = DateTime.UtcNow.AddMinutes(-10);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cacheFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private IptvEpgService Service()
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IIptvRepository)).Returns(_repository);
        provider.GetService(typeof(IIptvUpstreamResolver)).Returns(new IptvUpstreamResolver(new PlainSecretProtector()));
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        return new IptvEpgService(scopes, _upstream, Options.Create(new StoragePathsOptions { EpgCache = _cacheFolder }), NullLogger<IptvEpgService>.Instance, timeProvider: _clock);
    }

    private IptvPlaylist Tuner(bool useGuide = true, IEnumerable<(string Number, string Name)>? lineup = null)
    {
        var playlist = new IptvPlaylist { Name = "Antenna", SourceType = IptvSourceType.HdHomeRun, ServerUrl = HdHomeRunFixtures.Address, UseProviderGuide = useGuide };
        _repository.GetActiveEpgSourcesAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvEpgSource>());
        _repository.GetActivePlaylistsAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvPlaylist> { playlist });
        _repository.GetActiveChannelsAsync(Arg.Any<CancellationToken>()).Returns((lineup ?? [("2.1", "WCBS-HD"), ("7.1", "WABC-HD")])
            .Select(c => new IptvChannel { ExternalChannelId = c.Number, Name = c.Name, StreamUrl = $"http://192.168.1.50:5004/auto/v{c.Number}", PlaylistId = playlist.Id })
            .ToList());
        return playlist;
    }

    private string Feed(string title = "The News") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="2.1"><display-name>2.1</display-name><display-name>WCBS</display-name></channel>
          <channel id="station-7"><display-name>7.1 WABC</display-name></channel>
          <programme start="{Time(_start)}" stop="{Time(_start.AddHours(1))}" channel="2.1"><title>{title}</title></programme>
          <programme start="{Time(_start)}" stop="{Time(_start.AddHours(1))}" channel="station-7"><title>Eyewitness</title></programme>
        </tv>
        """;

    private void Silicondust(string feed) =>
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", HdHomeRunFixtures.Discover)
            .On(uri => uri.Host == "api.hdhomerun.com", feed, "application/xml");

    private int GuideDownloads => _upstream.Requests.Count(r => r.RequestUri!.Host == "api.hdhomerun.com");

    private static string Time(DateTime utc) => utc.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + " +0000";

    private static List<string> Titles(IptvEpgService service, string channel, DateTime from) =>
        service.GetProgramsForChannels([channel], from, from.AddHours(3))[channel].Select(p => p.Title).ToList();

    [Fact]
    public async Task The_guide_is_read_with_the_devices_authorization_and_reaches_its_channels_by_number_and_by_name()
    {
        var playlist = Tuner();
        Silicondust(Feed());
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        var request = _upstream.Requests.Single(r => r.RequestUri!.Host == "api.hdhomerun.com");
        request.RequestUri!.ToString().Should().Be("https://api.hdhomerun.com/api/xmltv?DeviceAuth=secret-auth");
        request.Headers.AcceptEncoding.ToString().Should().Contain("gzip");
        Titles(service, "2.1", _start).Should().Equal("The News");
        Titles(service, "7.1", _start).Should().Equal("Eyewitness");
        var stats = service.GetAllSyncStats()[playlist.Id];
        stats.Error.Should().BeNull();
        stats.MatchedChannels.Should().Be(2);
    }

    [Fact]
    public async Task A_compressed_guide_is_read_even_when_it_is_not_labelled_as_compressed()
    {
        Tuner();
        var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(Feed()), TestContext.Current.CancellationToken);
        }
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", HdHomeRunFixtures.Discover)
            .OnBytes(uri => uri.Host == "api.hdhomerun.com", compressed.ToArray());
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        Titles(service, "2.1", _start).Should().Equal("The News");
    }

    [Fact]
    public async Task The_guide_is_downloaded_about_once_a_day_and_the_last_one_is_used_in_between()
    {
        Tuner();
        Silicondust(Feed());
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromHours(19));
        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        GuideDownloads.Should().Be(1);
        Titles(service, "2.1", _start).Should().Equal("The News");

        _clock.Advance(TimeSpan.FromHours(10));
        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        GuideDownloads.Should().Be(2);
    }

    [Fact]
    public async Task A_guide_that_fails_to_download_keeps_what_was_read_before_and_is_tried_again_at_the_next_sync()
    {
        var playlist = Tuner();
        Silicondust(Feed());
        var service = Service();
        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromHours(30));
        _upstream.Fail(uri => uri.Host == "api.hdhomerun.com", new HttpRequestException("Silicondust is down"));

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        service.GetAllSyncStats()[playlist.Id].Error.Should().Be("Silicondust's guide could not be reached: Silicondust is down");
        Titles(service, "2.1", _start).Should().Equal("The News");

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        GuideDownloads.Should().Be(3);
    }

    [Fact]
    public async Task A_failure_never_carries_the_authorization_into_the_recorded_error()
    {
        var playlist = Tuner();
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", HdHomeRunFixtures.Discover)
            .Fail(uri => uri.Host == "api.hdhomerun.com", new HttpRequestException("failed for https://api.hdhomerun.com/api/xmltv?DeviceAuth=secret-auth"));
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        var error = service.GetAllSyncStats()[playlist.Id].Error;
        error.Should().NotBeNull().And.NotContain("secret-auth");
    }

    [Fact]
    public async Task A_device_that_gives_no_authorization_is_reported_and_nothing_is_downloaded()
    {
        var playlist = Tuner();
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", """{"FriendlyName":"Old"}""");
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        service.GetAllSyncStats()[playlist.Id].Error.Should().Contain("no authorization");
        GuideDownloads.Should().Be(0);
    }

    [Fact]
    public async Task The_guide_is_left_alone_when_the_admin_turned_it_off()
    {
        Tuner(useGuide: false);
        Silicondust(Feed());

        await Service().SyncEpgDataAsync(TestContext.Current.CancellationToken);

        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_device_with_no_channels_yet_is_not_asked_for_a_guide()
    {
        Tuner(lineup: []);
        Silicondust(Feed());

        await Service().SyncEpgDataAsync(TestContext.Current.CancellationToken);

        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_epg_source_the_admin_added_covers_a_channel_before_the_devices_guide_does()
    {
        var playlist = Tuner();
        var source = new IptvEpgSource { Name = "Mine", XmlTvUrl = "http://epg.example.com/guide.xml", IsActive = true };
        _repository.GetActiveEpgSourcesAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvEpgSource> { source });
        _upstream.On(uri => uri.Host == "epg.example.com", $"""<tv><programme start="{Time(_start)}" stop="{Time(_start.AddHours(1))}" channel="2.1"><title>My Source</title></programme></tv>""", "application/xml");
        Silicondust(Feed());
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        Titles(service, "2.1", _start).Should().Equal("My Source");
        Titles(service, "7.1", _start).Should().Equal("Eyewitness");
        service.GetAllSyncStats().Should().ContainKey(playlist.Id);
    }

    [Fact]
    public async Task A_changed_address_is_a_different_device_and_is_asked_again_at_once()
    {
        var playlist = Tuner();
        Silicondust(Feed());
        var service = Service();
        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        playlist.ServerUrl = "192.168.1.51";
        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        GuideDownloads.Should().Be(2);
    }
}

public static class EpgServiceCollection
{
    public const string Name = "The EPG service's shared guide cache";
}
