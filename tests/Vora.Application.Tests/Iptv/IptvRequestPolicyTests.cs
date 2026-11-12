using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class IptvRequestPolicyTests
{
    private readonly PlainSecretProtector _protector = new();

    private IptvUpstreamResolver Resolver() => new(_protector);

    private static IptvChannel Channel(string streamUrl, Guid playlistId, string? userAgent = null, string? referer = null) => new()
    {
        ExternalChannelId = "news",
        Name = "News",
        StreamUrl = streamUrl,
        PlaylistId = playlistId,
        UserAgent = userAgent,
        Referer = referer
    };

    [Fact]
    public void A_request_carries_the_configured_user_agent_and_referer()
    {
        var policy = new IptvRequestPolicy("IPTVSmartersPro", "https://portal.example.com/", null);

        using var request = policy.Get("https://iptv.example.com/list.m3u");

        request.Headers.GetValues("User-Agent").Should().Equal("IPTVSmartersPro");
        request.Headers.GetValues("Referer").Should().Equal("https://portal.example.com/");
        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public void Without_a_configured_user_agent_the_fallback_is_used_and_otherwise_the_client_default_stays()
    {
        using var audio = IptvRequestPolicy.None.Get("https://radio.example.com/live.mp3", "WinampMPEG/5.0");
        using var plain = IptvRequestPolicy.None.Get("https://radio.example.com/live.mp3");
        using var configured = new IptvRequestPolicy("VLC/3.0.20", null, null).Get("https://radio.example.com/live.mp3", "WinampMPEG/5.0");

        audio.Headers.GetValues("User-Agent").Should().Equal("WinampMPEG/5.0");
        plain.Headers.Contains("User-Agent").Should().BeFalse();
        configured.Headers.GetValues("User-Agent").Should().Equal("VLC/3.0.20");
    }

    [Fact]
    public void A_playlist_login_goes_only_to_the_playlist_s_own_host_and_port()
    {
        var basic = IptvBasicCredentials.ForUrl("https://iptv.example.com/list.m3u", "viewer", "example-password");
        var policy = new IptvRequestPolicy(null, null, basic);

        using var sameHost = policy.Get("https://IPTV.example.com/streams/news.m3u8");
        using var otherHost = policy.Get("https://cdn.example.net/streams/news.m3u8");
        using var otherPort = policy.Get("https://iptv.example.com:8443/streams/news.m3u8");

        sameHost.Headers.Authorization!.Scheme.Should().Be("Basic");
        sameHost.Headers.Authorization.Parameter.Should().Be(Convert.ToBase64String("viewer:example-password"u8.ToArray()));
        otherHost.Headers.Authorization.Should().BeNull();
        otherPort.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public void Header_values_lose_control_characters_so_they_cannot_add_headers()
    {
        var policy = new IptvRequestPolicy("Agent\r\nX-Injected: yes", "  https://portal.example.com/\n ", null);

        policy.UserAgent.Should().Be("AgentX-Injected: yes");
        policy.Referer.Should().Be("https://portal.example.com/");
        policy.FfmpegInputOptions().Should().Equal("-user_agent", "AgentX-Injected: yes", "-headers", "Referer: https://portal.example.com/\r\n");
    }

    [Fact]
    public void Ffmpeg_gets_no_options_when_nothing_is_configured()
    {
        IptvRequestPolicy.None.FfmpegInputOptions().Should().BeEmpty();
        IptvRequestPolicy.None.FfmpegInputUrl("https://iptv.example.com/a.ts").Should().Be("https://iptv.example.com/a.ts");
    }

    [Fact]
    public void A_channel_s_own_headers_win_over_the_playlist_s()
    {
        var playlist = new IptvPlaylist { Name = "Provider", M3uUrl = "https://iptv.example.com/list.m3u", UserAgent = "PlaylistAgent", Referer = "https://playlist.example.com/" };

        var own = Resolver().PolicyFor(playlist, Channel("https://iptv.example.com/a.ts", playlist.Id, userAgent: "ChannelAgent"));
        var inherited = Resolver().PolicyFor(playlist, Channel("https://iptv.example.com/b.ts", playlist.Id));

        own.UserAgent.Should().Be("ChannelAgent");
        own.Referer.Should().Be("https://playlist.example.com/");
        inherited.UserAgent.Should().Be("PlaylistAgent");
    }

    [Fact]
    public void A_free_playlist_has_no_policy_at_all()
    {
        var playlist = new IptvPlaylist { Name = "Free", M3uUrl = "https://iptv-org.github.io/iptv/index.m3u" };

        Resolver().PolicyFor(playlist, Channel("https://iptv.example.com/a.ts", playlist.Id)).Should().BeSameAs(IptvRequestPolicy.None);
    }

    [Fact]
    public void An_m3u_login_is_decrypted_for_the_playlist_host()
    {
        var playlist = new IptvPlaylist { Name = "Provider", M3uUrl = "https://iptv.example.com/list.m3u", Username = "viewer", PasswordCiphertext = _protector.Protect("example-password") };

        var policy = Resolver().PolicyFor(playlist);

        policy.Basic!.Host.Should().Be("iptv.example.com");
        policy.Basic.Password.Should().Be("example-password");
    }

    [Fact]
    public void An_xtream_account_never_sends_a_basic_login()
    {
        var playlist = new IptvPlaylist { Name = "Provider", SourceType = IptvSourceType.Xtream, ServerUrl = XtreamFixtures.Server, Username = "viewer", PasswordCiphertext = _protector.Protect("example-password") };

        Resolver().PolicyFor(playlist).Basic.Should().BeNull();
    }

    [Fact]
    public void A_password_the_server_can_no_longer_read_is_a_readable_error()
    {
        var playlist = new IptvPlaylist { Name = "Provider", M3uUrl = "https://iptv.example.com/list.m3u", Username = "viewer", PasswordCiphertext = "written-with-other-keys" };

        var act = () => Resolver().PolicyFor(playlist);

        act.Should().Throw<IptvSourceException>().WithMessage(IptvUpstreamResolver.UnreadablePasswordMessage);
    }
}
