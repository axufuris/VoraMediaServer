using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class IptvUpstreamResolverTests
{
    private readonly PlainSecretProtector _protector = new();

    private IptvPlaylist Xtream(string password = XtreamFixtures.Password) => new()
    {
        Name = "Provider",
        SourceType = IptvSourceType.Xtream,
        ServerUrl = XtreamFixtures.Server,
        Username = XtreamFixtures.Username,
        PasswordCiphertext = _protector.Protect(password),
        UserAgent = "IPTVSmartersPro"
    };

    private static IptvChannel Channel(Guid playlistId, string streamUrl) => new()
    {
        ExternalChannelId = "bbc1.uk",
        Name = "BBC One",
        StreamUrl = streamUrl,
        PlaylistId = playlistId
    };

    [Fact]
    public void An_xtream_reference_becomes_the_live_address_with_the_account_in_it()
    {
        var playlist = Xtream();

        var upstream = new IptvUpstreamResolver(_protector).Resolve(playlist, Channel(playlist.Id, "xtream:1001.m3u8"));

        upstream.Url.Should().Be("http://provider.example.com:8080/live/viewer/example-password/1001.m3u8");
        upstream.Policy.UserAgent.Should().Be("IPTVSmartersPro");
    }

    [Fact]
    public void A_new_password_is_used_without_reloading_the_channels()
    {
        var playlist = Xtream();
        var channel = Channel(playlist.Id, "xtream:1001.ts");
        playlist.PasswordCiphertext = _protector.Protect("changed password");

        var upstream = new IptvUpstreamResolver(_protector).Resolve(playlist, channel);

        upstream.Url.Should().Be("http://provider.example.com:8080/live/viewer/changed%20password/1001.ts");
        channel.StreamUrl.Should().Be("xtream:1001.ts");
    }

    [Fact]
    public void An_ordinary_address_is_used_as_it_is()
    {
        var playlist = new IptvPlaylist { Name = "Free", M3uUrl = "https://iptv.example.com/list.m3u" };

        new IptvUpstreamResolver(_protector).Resolve(playlist, Channel(playlist.Id, "https://iptv.example.com/news.m3u8")).Url
            .Should().Be("https://iptv.example.com/news.m3u8");
    }

    [Fact]
    public void An_xtream_reference_without_its_account_cannot_be_played()
    {
        var playlist = new IptvPlaylist { Name = "Switched to M3U", M3uUrl = "https://iptv.example.com/list.m3u" };

        var act = () => new IptvUpstreamResolver(_protector).Resolve(playlist, Channel(playlist.Id, "xtream:1001.ts"));

        act.Should().Throw<IptvSourceException>();
    }

    [Theory]
    [InlineData("provider.example.com:8080", "http://provider.example.com:8080")]
    [InlineData("http://provider.example.com:8080/", "http://provider.example.com:8080")]
    [InlineData("https://provider.example.com/player_api.php?username=a&password=b", "https://provider.example.com")]
    [InlineData("http://provider.example.com:8080/get.php", "http://provider.example.com:8080")]
    [InlineData("http://provider.example.com/panel/", "http://provider.example.com/panel")]
    [InlineData("ftp://provider.example.com", null)]
    [InlineData("   ", null)]
    public void A_server_address_is_normalised(string input, string? expected)
    {
        XtreamAccount.NormalizeServerUrl(input).Should().Be(expected);
    }

    [Fact]
    public void The_account_builds_the_api_and_guide_addresses()
    {
        var account = new XtreamAccount(XtreamFixtures.Server, "viewer", "p&ss");

        account.PlayerApiUrl().Should().Be("http://provider.example.com:8080/player_api.php?username=viewer&password=p%26ss");
        account.PlayerApiUrl("get_live_streams").Should().EndWith("&action=get_live_streams");
        account.XmlTvUrl().Should().Be("http://provider.example.com:8080/xmltv.php?username=viewer&password=p%26ss");
    }

    [Theory]
    [InlineData("xtream:1001.m3u8", true, "1001", "m3u8")]
    [InlineData("xtream:abc_9.ts", true, "abc_9", "ts")]
    [InlineData("xtream:../1.ts", false, "", "")]
    [InlineData("https://iptv.example.com/1.ts", false, "", "")]
    public void A_stream_reference_round_trips(string reference, bool valid, string streamId, string extension)
    {
        XtreamStreamReference.TryParse(reference, out var id, out var ext).Should().Be(valid);
        id.Should().Be(streamId);
        ext.Should().Be(extension);
    }
}
