using System.Net;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class IptvChannelLoaderTests
{
    private readonly PlainSecretProtector _protector = new();
    private readonly StubUpstream _upstream = new();

    private IptvChannelLoader Loader() => new(_upstream, new IptvUpstreamResolver(_protector));

    private IptvPlaylist Xtream(string? userAgent = null) => new()
    {
        Name = "Paid",
        SourceType = IptvSourceType.Xtream,
        ServerUrl = XtreamFixtures.Server,
        Username = XtreamFixtures.Username,
        PasswordCiphertext = _protector.Protect(XtreamFixtures.Password),
        UserAgent = userAgent
    };

    [Fact]
    public async Task An_xtream_account_loads_its_live_streams_with_their_categories_in_the_preferred_format()
    {
        _upstream.OnAction(null, XtreamFixtures.ActiveAccount)
            .OnAction("get_live_categories", XtreamFixtures.Categories)
            .OnAction("get_live_streams", XtreamFixtures.LiveStreams);

        var load = await Loader().LoadAsync(Xtream(), TestContext.Current.CancellationToken);

        load.Account!.MaxConnections.Should().Be(2);
        load.Channels.Should().HaveCount(4);
        load.Channels.Should().OnlyContain(c => c.StreamUrl.EndsWith(".m3u8"));
        load.Channels.Single(c => c.StreamUrl == "xtream:1003.m3u8").GroupTitle.Should().Be("News");
        _upstream.Requests.Select(r => r.RequestUri!.ToString()).Should().Equal(
            "http://provider.example.com:8080/player_api.php?username=viewer&password=example-password",
            "http://provider.example.com:8080/player_api.php?username=viewer&password=example-password&action=get_live_categories",
            "http://provider.example.com:8080/player_api.php?username=viewer&password=example-password&action=get_live_streams");
    }

    [Fact]
    public async Task An_account_limited_to_ts_gets_ts_references()
    {
        _upstream.OnAction(null, XtreamFixtures.TransportStreamOnlyAccount)
            .OnAction("get_live_categories", "[]")
            .OnAction("get_live_streams", XtreamFixtures.LiveStreams);

        var load = await Loader().LoadAsync(Xtream(), TestContext.Current.CancellationToken);

        load.Channels.Should().OnlyContain(c => c.StreamUrl.EndsWith(".ts"));
        load.Channels.Should().OnlyContain(c => c.GroupTitle == null);
    }

    [Fact]
    public async Task A_rejected_login_stops_before_the_channel_list()
    {
        _upstream.OnAction(null, XtreamFixtures.RejectedAccount);

        var act = async () => await Loader().LoadAsync(Xtream(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XtreamAuthenticationException>().WithMessage("The provider rejected the username or password.");
        _upstream.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task An_expired_account_is_reported_with_what_the_provider_said_about_it()
    {
        _upstream.OnAction(null, XtreamFixtures.ExpiredStatusAccount);

        var act = async () => await Loader().LoadAsync(Xtream(), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<XtreamAccountException>().WithMessage("The provider account has expired.");
        thrown.Which.Account.Status.Should().Be("Expired");
        _upstream.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task An_http_failure_is_a_readable_error_without_the_address()
    {
        _upstream.On(_ => true, "Forbidden", "text/plain", HttpStatusCode.Forbidden);

        var act = async () => await Loader().LoadAsync(Xtream(), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<IptvSourceException>();
        thrown.Which.Message.Should().Contain("HTTP 403").And.NotContain("example-password").And.NotContain("provider.example.com");
    }

    [Fact]
    public async Task Every_xtream_request_carries_the_configured_user_agent()
    {
        _upstream.OnAction(null, XtreamFixtures.ActiveAccount)
            .OnAction("get_live_categories", "[]")
            .OnAction("get_live_streams", "[]");

        await Loader().LoadAsync(Xtream(userAgent: "IPTVSmartersPro"), TestContext.Current.CancellationToken);

        _upstream.Requests.Should().HaveCount(3).And.OnlyContain(r => r.Headers.UserAgent.ToString() == "IPTVSmartersPro");
    }

    [Fact]
    public async Task An_m3u_download_sends_the_login_and_headers_to_the_playlist_host()
    {
        _upstream.On(uri => uri.Host == "iptv.example.com", "#EXTM3U\n#EXTINF:-1 tvg-id=\"a\",A\nhttps://iptv.example.com/a.m3u8\n", "audio/x-mpegurl");
        var playlist = new IptvPlaylist
        {
            Name = "Login",
            M3uUrl = "https://iptv.example.com/list.m3u",
            Username = "viewer",
            PasswordCiphertext = _protector.Protect("example-password"),
            Referer = "https://portal.example.com/"
        };

        var load = await Loader().LoadAsync(playlist, TestContext.Current.CancellationToken);

        load.Channels.Should().ContainSingle();
        var request = _upstream.Requests.Single();
        request.Headers.Authorization!.Parameter.Should().Be(Convert.ToBase64String("viewer:example-password"u8.ToArray()));
        request.Headers.Referrer.Should().Be(new Uri("https://portal.example.com/"));
        request.Headers.UserAgent.ToString().Should().Be("DefaultAgent/1.0");
    }

    [Fact]
    public async Task A_free_m3u_download_is_sent_as_before()
    {
        _upstream.On(_ => true, "#EXTM3U\n#EXTINF:-1 tvg-id=\"a\",A\nhttps://iptv.example.com/a.m3u8\n", "audio/x-mpegurl");

        await Loader().LoadAsync(new IptvPlaylist { Name = "Free", M3uUrl = "https://iptv-org.github.io/iptv/index.m3u" }, TestContext.Current.CancellationToken);

        var request = _upstream.Requests.Single();
        request.Headers.Authorization.Should().BeNull();
        request.Headers.Contains("Referer").Should().BeFalse();
        request.Headers.UserAgent.ToString().Should().Be("DefaultAgent/1.0");
    }
}
