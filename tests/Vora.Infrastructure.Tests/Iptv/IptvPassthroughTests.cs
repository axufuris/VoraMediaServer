using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;
using Vora.Infrastructure.Iptv;

namespace Vora.Infrastructure.Tests.Iptv;

public class IptvPassthroughTests
{
    private const string Password = "example-password";

    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
    private readonly IIptvRepository _repository = Substitute.For<IIptvRepository>();
    private readonly IUserManager _users = Substitute.For<IUserManager>();
    private readonly TunerRegistry _tuners = new();
    private readonly Upstream _upstream = new();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _profileId = Guid.NewGuid();

    public IptvPassthroughTests()
    {
        AllowEverything();
    }

    private void AllowEverything(bool profileAllowed = true, Guid? playlistId = null) =>
        _users.GetUserAccountAsync(_accountId).Returns(new UserVM
        {
            Id = _accountId,
            HasAllIptvAccess = true,
            Profiles = new List<UserProfileVM>
            {
                new() { Id = _profileId, HasAllIptvAccess = profileAllowed, AllowedIptvPlaylistIds = playlistId is { } id && profileAllowed ? new List<Guid> { id } : new List<Guid>() }
            }
        });

    private DataProtectionPassthroughTokenProtector Tokens() => new(_dataProtection);

    private IptvPassthroughService Service()
    {
        var secrets = new DataProtectionIptvSecretProtector(_dataProtection);
        return new IptvPassthroughService(_repository, _users, _upstream, _tuners, new IptvUpstreamResolver(secrets), Tokens(), NullLogger<IptvPassthroughService>.Instance);
    }

    private IptvChannel XtreamChannel(int maxConnections = 0, string extension = "m3u8", string? userAgent = "IPTVSmartersPro")
    {
        var secrets = new DataProtectionIptvSecretProtector(_dataProtection);
        var playlist = new IptvPlaylist
        {
            Name = "Paid",
            SourceType = IptvSourceType.Xtream,
            ServerUrl = "http://provider.example.com:8080",
            Username = "viewer",
            PasswordCiphertext = secrets.Protect(Password),
            UserAgent = userAgent,
            Referer = "https://portal.example.com/",
            AccountMaxConnections = maxConnections == 0 ? null : maxConnections,
            TunerProfile = new IptvTunerProfile()
        };
        var channel = new IptvChannel { ExternalChannelId = "bbc1.uk", Name = "BBC One", StreamUrl = $"xtream:1001.{extension}", PlaylistId = playlist.Id, Playlist = playlist };
        _repository.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        return channel;
    }

    private IptvChannel FreeRadioChannel(int maxConcurrentStreams = 0)
    {
        var playlist = new IptvPlaylist { Name = "Radio", M3uUrl = "https://radio.example.com/list.m3u", DefaultChannelKind = IptvChannelKind.Radio, TunerProfile = new IptvTunerProfile { MaxConcurrentStreams = maxConcurrentStreams } };
        var channel = new IptvChannel { ExternalChannelId = "jazz", Name = "Jazz", StreamUrl = "https://radio.example.com/jazz.mp3", PlaylistId = playlist.Id, Playlist = playlist, Kind = IptvChannelKind.Radio };
        _repository.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        return channel;
    }

    private static string Token(string url) => url[(url.IndexOf("t=", StringComparison.Ordinal) + 2)..];

    [Fact]
    public void A_token_reads_back_until_it_expires()
    {
        var tokens = Tokens();

        var token = tokens.Protect("p|payload", DateTimeOffset.UtcNow.AddMinutes(5));

        tokens.TryUnprotect(token, out var payload).Should().BeTrue();
        payload.Should().Be("p|payload");
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        var tokens = Tokens();

        var token = tokens.Protect("p|payload", DateTimeOffset.UtcNow.AddSeconds(-1));

        tokens.TryUnprotect(token, out _).Should().BeFalse();
    }

    [Fact]
    public void A_tampered_token_is_refused()
    {
        var tokens = Tokens();
        var token = tokens.Protect("p|payload", DateTimeOffset.UtcNow.AddMinutes(5));
        var middle = token.Length / 2;
        var tampered = token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..];

        tokens.TryUnprotect(tampered, out _).Should().BeFalse();
        tokens.TryUnprotect("not-a-token", out _).Should().BeFalse();
        tokens.TryUnprotect(string.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void A_token_from_another_server_is_refused()
    {
        var token = new DataProtectionPassthroughTokenProtector(new EphemeralDataProtectionProvider()).Protect("p|payload", DateTimeOffset.UtcNow.AddMinutes(5));

        Tokens().TryUnprotect(token, out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_playlist_address_a_client_gets_reveals_nothing_about_the_provider()
    {
        var channel = XtreamChannel();

        var start = await Service().StartPassthroughAsync(channel.Id, _accountId, _profileId);

        start.StreamType.Should().Be(IptvPassthroughService.StreamTypeHls);
        var token = Token(start.Url);
        token.Should().NotContain("provider").And.NotContain("viewer").And.NotContain(Password);
        Encoding.UTF8.GetString(Convert.FromBase64String(Base64(token))).Should().NotContain("provider.example.com").And.NotContain(Password);
    }

    [Fact]
    public async Task Playlist_and_segment_fetches_go_out_with_the_provider_s_headers_and_rewritten_tokens()
    {
        var channel = XtreamChannel();
        _upstream.Respond(uri => uri.AbsolutePath.EndsWith("/1001.m3u8"), "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\n/hls/abc/1001_1.ts\n", "application/vnd.apple.mpegurl");
        _upstream.Respond(uri => uri.AbsolutePath.EndsWith("_1.ts"), "segment-bytes", "video/mp2t");
        var service = Service();

        var start = await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        var playlist = await service.GetRewrittenPlaylistAsync(Token(start.Url));

        playlist.Should().NotBeNull();
        playlist!.Content.Should().NotContain("/hls/abc").And.Contain("segment?t=");
        var segmentLine = playlist.Content.Split('\n').Single(l => l.StartsWith("segment?t=", StringComparison.Ordinal));
        using var segment = await service.FetchSegmentAsync(Token(segmentLine), TestContext.Current.CancellationToken);

        segment!.StatusCode.Should().Be(HttpStatusCode.OK);
        _upstream.Requests.Select(r => r.RequestUri!.ToString()).Should().Equal(
            "http://provider.example.com:8080/live/viewer/example-password/1001.m3u8",
            "http://provider.example.com:8080/hls/abc/1001_1.ts");
        _upstream.Requests.Should().OnlyContain(r => r.Headers.UserAgent.ToString() == "IPTVSmartersPro" && r.Headers.Referrer == new Uri("https://portal.example.com/"));
    }

    [Fact]
    public async Task A_segment_token_is_not_accepted_as_a_playlist_token()
    {
        var channel = XtreamChannel();
        _upstream.Respond(uri => uri.AbsolutePath.EndsWith("/1001.m3u8"), "#EXTM3U\n#EXTINF:4,\nseg1.ts\n", "application/vnd.apple.mpegurl");
        var service = Service();
        var start = await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        var playlist = await service.GetRewrittenPlaylistAsync(Token(start.Url));
        var segmentToken = Token(playlist!.Content.Split('\n').Single(l => l.StartsWith("segment?t=", StringComparison.Ordinal)));

        (await service.GetRewrittenPlaylistAsync(segmentToken)).Should().BeNull();
        (await service.FetchAudioStreamAsync(segmentToken, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Radio_without_a_configured_user_agent_still_uses_the_winamp_one()
    {
        var channel = FreeRadioChannel();
        _upstream.Respond(_ => true, "mp3-bytes", "audio/mpeg");
        var service = Service();

        var start = await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        using var audio = await service.FetchAudioStreamAsync(Token(start.Url), TestContext.Current.CancellationToken);

        start.StreamType.Should().Be(IptvPassthroughService.StreamTypeAudio);
        _upstream.Requests.Single().Headers.UserAgent.ToString().Should().Be("WinampMPEG/5.0");
        _upstream.Requests.Single().Headers.GetValues("Icy-MetaData").Should().Equal("0");
    }

    [Fact]
    public async Task Radio_on_a_provider_with_a_user_agent_sends_that_one()
    {
        var channel = XtreamChannel(extension: "mp3");
        _upstream.Respond(_ => true, "mp3-bytes", "audio/mpeg");
        var service = Service();

        var start = await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        using var audio = await service.FetchAudioStreamAsync(Token(start.Url), TestContext.Current.CancellationToken);

        _upstream.Requests.Single().Headers.UserAgent.ToString().Should().Be("IPTVSmartersPro");
        _upstream.Requests.Single().RequestUri!.ToString().Should().Be("http://provider.example.com:8080/live/viewer/example-password/1001.mp3");
    }

    [Fact]
    public async Task Radio_on_a_playlist_with_a_connection_limit_holds_a_tuner_until_the_stream_ends()
    {
        var channel = FreeRadioChannel(maxConcurrentStreams: 1);
        _upstream.Respond(_ => true, "mp3-bytes", "audio/mpeg");
        var service = Service();

        var start = await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1);

        var second = async () => await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        await second.Should().ThrowAsync<TunerLimitReachedException>();

        var audio = await service.FetchAudioStreamAsync(Token(start.Url), TestContext.Current.CancellationToken);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1);
        audio!.Dispose();

        _tuners.ActiveCount(channel.PlaylistId).Should().Be(0);
    }

    [Fact]
    public async Task Radio_on_a_playlist_without_a_limit_takes_no_tuner()
    {
        var channel = FreeRadioChannel();
        _upstream.Respond(_ => true, "mp3-bytes", "audio/mpeg");

        await Service().StartPassthroughAsync(channel.Id, _accountId, _profileId);

        _tuners.ActiveCount(channel.PlaylistId).Should().Be(0);
    }

    [Fact]
    public async Task The_account_s_connection_limit_applies_when_the_admin_left_it_at_zero()
    {
        var channel = XtreamChannel(maxConnections: 1);
        var service = Service();

        await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);
        var second = async () => await service.StartPassthroughAsync(channel.Id, _accountId, _profileId);

        await second.Should().ThrowAsync<TunerLimitReachedException>();
    }

    [Fact]
    public async Task Passthrough_follows_the_profile_allowlist()
    {
        var channel = XtreamChannel();
        AllowEverything(profileAllowed: false);

        var act = async () => await Service().StartPassthroughAsync(channel.Id, _accountId, _profileId);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Passthrough_allows_a_profile_whose_allowlist_has_the_playlist()
    {
        var channel = XtreamChannel();
        AllowEverything(profileAllowed: true, playlistId: channel.PlaylistId);

        var start = await Service().StartPassthroughAsync(channel.Id, _accountId, _profileId);

        start.Url.Should().StartWith("/api/iptv/passthrough/playlist.m3u8?t=");
    }

    private static string Base64(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }

    private sealed class Upstream : HttpMessageHandler, IHttpClientFactory
    {
        private readonly List<(Func<Uri, bool> Matches, string Body, string ContentType)> _routes = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public void Respond(Func<Uri, bool> matches, string body, string contentType) => _routes.Add((matches, body, contentType));

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(this, disposeHandler: false);
            client.DefaultRequestHeaders.Add("User-Agent", "DefaultAgent/1.0");
            return client;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var route = _routes.FirstOrDefault(r => r.Matches(request.RequestUri!));
            var response = route.Body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(route.Body, Encoding.UTF8, route.ContentType) };
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
