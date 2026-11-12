using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public sealed class IptvProviderGuideTests : IDisposable
{
    private const string Guide = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="bbc1.uk"><display-name>BBC One</display-name></channel>
          <programme start="{START}" stop="{STOP}" channel="bbc1.uk"><title>The News</title></programme>
        </tv>
        """;

    private readonly string _cacheFolder = Path.Combine(Path.GetTempPath(), "vora-provider-guide-" + Guid.NewGuid().ToString("N"));
    private readonly IIptvRepository _repository = Substitute.For<IIptvRepository>();
    private readonly PlainSecretProtector _protector = new();
    private readonly StubUpstream _upstream = new();

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
        provider.GetService(typeof(IIptvUpstreamResolver)).Returns(new IptvUpstreamResolver(_protector));
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        return new IptvEpgService(scopes, _upstream, Options.Create(new StoragePathsOptions { EpgCache = _cacheFolder }), NullLogger<IptvEpgService>.Instance);
    }

    private IptvPlaylist Xtream(bool useProviderGuide) => new()
    {
        Name = "Paid",
        SourceType = IptvSourceType.Xtream,
        ServerUrl = XtreamFixtures.Server,
        Username = XtreamFixtures.Username,
        PasswordCiphertext = _protector.Protect(XtreamFixtures.Password),
        UserAgent = "IPTVSmartersPro",
        UseProviderGuide = useProviderGuide
    };

    [Fact]
    public async Task An_xtream_account_s_own_guide_is_read_with_its_login_and_fills_its_channels()
    {
        var playlist = Xtream(useProviderGuide: true);
        var start = DateTime.UtcNow.AddMinutes(-10);
        _repository.GetActiveEpgSourcesAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvEpgSource>());
        _repository.GetActivePlaylistsAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvPlaylist> { playlist });
        _repository.GetActiveChannelsAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvChannel>
        {
            new() { ExternalChannelId = "bbc1.uk", Name = "BBC One", StreamUrl = "xtream:1001.ts", PlaylistId = playlist.Id },
            new() { ExternalChannelId = "bbc1.uk@1002", Name = "BBC One FHD", StreamUrl = "xtream:1002.ts", PlaylistId = playlist.Id }
        });
        _upstream.On(uri => uri.AbsolutePath == "/xmltv.php", Guide.Replace("{START}", XmlTime(start)).Replace("{STOP}", XmlTime(start.AddHours(1))), "application/xml");
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        var request = _upstream.Requests.Single();
        request.RequestUri!.ToString().Should().Be("http://provider.example.com:8080/xmltv.php?username=viewer&password=example-password");
        request.Headers.UserAgent.ToString().Should().Be("IPTVSmartersPro");
        var programs = service.GetProgramsForChannels(new List<string> { "bbc1.uk", "bbc1.uk@1002" }, start.AddMinutes(-5), start.AddHours(2));
        programs["bbc1.uk"].Should().ContainSingle(p => p.Title == "The News");
        programs["bbc1.uk@1002"].Should().ContainSingle(p => p.Title == "The News");
        service.GetAllSyncStats()[playlist.Id].Error.Should().BeNull();
    }

    [Fact]
    public async Task The_provider_guide_is_skipped_when_the_admin_turned_it_off()
    {
        _repository.GetActiveEpgSourcesAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvEpgSource>());
        _repository.GetActivePlaylistsAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvPlaylist> { Xtream(useProviderGuide: false) });

        await Service().SyncEpgDataAsync(TestContext.Current.CancellationToken);

        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_provider_guide_is_recorded_without_the_address()
    {
        var playlist = Xtream(useProviderGuide: true);
        _repository.GetActiveEpgSourcesAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvEpgSource>());
        _repository.GetActivePlaylistsAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvPlaylist> { playlist });
        _repository.GetActiveChannelsAsync(Arg.Any<CancellationToken>()).Returns(new List<IptvChannel>());
        var service = Service();

        await service.SyncEpgDataAsync(TestContext.Current.CancellationToken);

        service.GetAllSyncStats()[playlist.Id].Error.Should().Be("The provider's guide answered HTTP 404.");
    }

    private static string XmlTime(DateTime utc) => utc.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + " +0000";
}
