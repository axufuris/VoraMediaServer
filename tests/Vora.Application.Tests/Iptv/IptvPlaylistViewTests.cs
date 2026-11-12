using System.Text.Json;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class IptvPlaylistViewTests
{
    private static IptvPlaylist WithChannels(IptvPlaylist playlist, params string[] streamUrls)
    {
        var n = 0;
        foreach (var url in streamUrls)
        {
            playlist.Channels.Add(new IptvChannel { ExternalChannelId = $"ch{n++}", Name = "Channel", StreamUrl = url, PlaylistId = playlist.Id });
        }
        return playlist;
    }

    private static IptvPlaylist Xtream() => WithChannels(new IptvPlaylist
    {
        Name = "Paid",
        SourceType = IptvSourceType.Xtream,
        ServerUrl = "http://provider.example.com:8080",
        Username = "viewer",
        PasswordCiphertext = "ciphertext",
        UserAgent = "IPTVSmartersPro",
        AccountStatus = "Active",
        AccountMaxConnections = 2,
        LastError = "The provider answered HTTP 502.",
        TunerProfile = new IptvTunerProfile { MaxConcurrentStreams = 0 }
    }, "xtream:1001.m3u8", "xtream:1002.m3u8");

    [Fact]
    public void An_xtream_account_reaches_clients_without_any_address_or_secret()
    {
        var playlist = Xtream();

        var view = IptvPlaylistView.ForClient(playlist);

        view.M3uUrl.Should().Be("http://provider.example.com");
        view.ServerUrl.Should().BeNull();
        view.Username.Should().BeNull();
        view.PasswordIsSet.Should().BeFalse();
        view.UserAgent.Should().BeNull();
        view.LastError.Should().BeNull();
        view.AccountStatus.Should().BeNull();
        view.Channels.Select(c => c.StreamUrl).Should().Equal(playlist.Channels.Select(c => "vora-channel:" + c.Id));
        JsonSerializer.Serialize(view).Should().NotContain("viewer").And.NotContain("ciphertext").And.NotContain("xtream:").And.NotContain(":8080").And.NotContain("Smarters");
    }

    [Theory]
    [InlineData("http://provider.example.com/get.php?username=viewer&password=example-password&type=m3u_plus")]
    [InlineData("http://viewer:example-password@provider.example.com/list.m3u")]
    public void An_m3u_address_with_credentials_is_reduced_to_its_host(string m3uUrl)
    {
        var playlist = WithChannels(new IptvPlaylist { Name = "Paid M3U", M3uUrl = m3uUrl }, "http://provider.example.com/live/viewer/example-password/1.ts", "http://provider.example.com/live/viewer/example-password/2.ts");

        var view = IptvPlaylistView.ForClient(playlist);

        view.M3uUrl.Should().Be("http://provider.example.com");
        view.Channels.Should().OnlyContain(c => c.StreamUrl.StartsWith("vora-channel:"));
        view.Channels.Select(c => c.StreamUrl).Should().OnlyHaveUniqueItems();
        JsonSerializer.Serialize(view).Should().NotContain("example-password");
    }

    [Fact]
    public void An_m3u_with_a_login_or_headers_is_treated_as_authenticated()
    {
        var withLogin = WithChannels(new IptvPlaylist { Name = "Login", M3uUrl = "https://iptv.example.com/list.m3u", Username = "viewer", PasswordCiphertext = "ciphertext" }, "https://iptv.example.com/a.ts");
        var withReferer = WithChannels(new IptvPlaylist { Name = "Referer", M3uUrl = "https://iptv.example.com/list.m3u", Referer = "https://portal.example.com/" }, "https://iptv.example.com/a.ts");

        IptvPlaylistView.ForClient(withLogin).Channels.Single().StreamUrl.Should().StartWith("vora-channel:");
        IptvPlaylistView.ForClient(withReferer).Channels.Single().StreamUrl.Should().StartWith("vora-channel:");
        IptvPlaylistView.ForClient(withLogin).M3uUrl.Should().Be("https://iptv.example.com");
    }

    [Fact]
    public void A_free_playlist_keeps_its_addresses_so_older_apps_can_still_play_radio_directly()
    {
        var playlist = WithChannels(new IptvPlaylist { Name = "Radio", M3uUrl = "https://de1.api.radio-browser.info/m3u/stations/bycountry/germany", DefaultChannelKind = IptvChannelKind.Radio, LastError = "HTTP 500" },
            "https://stream.example.com/radio1.mp3", "https://stream.example.com/radio2.aac");

        var view = IptvPlaylistView.ForClient(playlist);

        view.M3uUrl.Should().Be("https://de1.api.radio-browser.info/m3u/stations/bycountry/germany");
        view.Channels.Select(c => c.StreamUrl).Should().Equal("https://stream.example.com/radio1.mp3", "https://stream.example.com/radio2.aac");
        view.LastError.Should().BeNull();
    }

    [Fact]
    public void A_free_playlist_hides_only_the_channels_whose_own_address_carries_credentials()
    {
        var playlist = WithChannels(new IptvPlaylist { Name = "Mixed", M3uUrl = "https://lists.example.com/mixed.m3u" },
            "https://stream.example.com/free.m3u8", "https://cdn.example.com/paid.m3u8?token=abc123");

        var view = IptvPlaylistView.ForClient(playlist);

        view.Channels[0].StreamUrl.Should().Be("https://stream.example.com/free.m3u8");
        view.Channels[1].StreamUrl.Should().StartWith("vora-channel:");
    }

    [Fact]
    public void Admins_see_the_account_details_but_never_the_password()
    {
        var playlist = Xtream();

        var view = IptvPlaylistView.ForAdmin(playlist);

        view.SourceType.Should().Be("Xtream");
        view.ServerUrl.Should().Be("http://provider.example.com:8080");
        view.Username.Should().Be("viewer");
        view.PasswordIsSet.Should().BeTrue();
        view.ConnectionLimit.Should().Be(2);
        view.LastError.Should().Be("The provider answered HTTP 502.");
        JsonSerializer.Serialize(view).Should().NotContain("ciphertext");
    }

    [Theory]
    [InlineData(0, null, 0)]
    [InlineData(0, 2, 2)]
    [InlineData(3, 2, 3)]
    [InlineData(1, null, 1)]
    public void The_connection_limit_is_the_admin_s_unless_left_at_zero(int maxConcurrentStreams, int? accountMax, int expected)
    {
        IptvConnectionLimit.Of(maxConcurrentStreams, accountMax).Should().Be(expected);
    }
}
