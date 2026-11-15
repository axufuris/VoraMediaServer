using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Podcasts;

namespace Vora.Application.Tests.Podcasts;

public class PodcastStreamResolverTests
{
    // The chain from The Boyscast's feed, shortened: a tracker prefix in front
    // of another, then the host's own address, then the file on its CDN.
    private const string Enclosure = "https://dts.podtrac.example/redirect.mp3/pscrb.example/rss/p/traffic.libsyn.example/secure/ep375.mp3?dest-id=4724197";
    private const string Tracker2 = "https://pscrb.example/rss/p/traffic.libsyn.example/secure/ep375.mp3?dest-id=4724197";
    private const string Host = "https://traffic.libsyn.example/secure/ep375.mp3?dest-id=4724197";
    private const string File = "https://delivery-edge.libsyn.example/shows/547682/items/43744024/ep375.mp3";

    private sealed class Web : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Answers { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void Redirect(string from, string to, HttpStatusCode status = HttpStatusCode.Found) =>
            Answers[from] = () => new HttpResponseMessage(status) { Headers = { Location = new Uri(to, UriKind.RelativeOrAbsolute) } };

        public void Serve(string url, HttpStatusCode status = HttpStatusCode.PartialContent) =>
            Answers[url] = () => new HttpResponseMessage(status) { Content = new ByteArrayContent([0xFF]) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var url = request.RequestUri!.AbsoluteUri;
            if (!Answers.TryGetValue(url, out var answer)) throw new HttpRequestException($"No route to {request.RequestUri.Host}");
            return Task.FromResult(answer());
        }
    }

    private readonly Web _web = new();
    private readonly Dictionary<string, IPAddress[]> _dns = new(StringComparer.OrdinalIgnoreCase);

    private PodcastStreamResolver Resolver()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PodcastStreamResolver.HttpClientName).Returns(_ => new HttpClient(_web, disposeHandler: false));
        return new PodcastStreamResolver(factory, NullLogger<PodcastStreamResolver>.Instance, (host, _) =>
            Task.FromResult(_dns.TryGetValue(host, out var addresses) ? addresses : [IPAddress.Parse("203.0.113.7")]));
    }

    private void TheBoyscastChain()
    {
        _web.Redirect(Enclosure, Tracker2);
        _web.Redirect(Tracker2, Host, HttpStatusCode.MovedPermanently);
        _web.Redirect(Host, File, HttpStatusCode.TemporaryRedirect);
        _web.Serve(File);
    }

    [Fact]
    public async Task Follows_the_tracker_redirects_to_the_file()
    {
        TheBoyscastChain();

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().Be(File);
        _web.Requests.Select(r => r.RequestUri!.Host).Should().Equal("dts.podtrac.example", "pscrb.example", "traffic.libsyn.example", "delivery-edge.libsyn.example");
    }

    [Fact]
    public async Task A_relative_location_is_read_against_the_hop_that_sent_it()
    {
        _web.Redirect(Enclosure, "/items/ep375.mp3");
        _web.Serve("https://dts.podtrac.example/items/ep375.mp3", HttpStatusCode.OK);

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().Be("https://dts.podtrac.example/items/ep375.mp3");
    }

    [Fact]
    public async Task An_address_with_no_redirect_is_its_own_answer()
    {
        _web.Serve(File, HttpStatusCode.OK);

        var url = await Resolver().ResolveAsync(File, null, TestContext.Current.CancellationToken);

        url.Should().Be(File);
    }

    [Fact]
    public async Task Each_hop_asks_for_the_first_byte_with_the_players_user_agent()
    {
        TheBoyscastChain();

        await Resolver().ResolveAsync(Enclosure, "Mozilla/5.0 (Windows NT 10.0) Edg/142.0", TestContext.Current.CancellationToken);

        _web.Requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Get);
            r.Headers.Range!.ToString().Should().Be("bytes=0-0");
            r.Headers.UserAgent.ToString().Should().Be("Mozilla/5.0 (Windows NT 10.0) Edg/142.0");
        });
    }

    [Fact]
    public async Task A_chain_that_ends_in_an_error_resolves_to_nothing()
    {
        _web.Redirect(Enclosure, Host);
        _web.Serve(Host, HttpStatusCode.NotFound);

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().BeNull();
    }

    [Fact]
    public async Task A_hop_that_cannot_be_reached_resolves_to_nothing()
    {
        _web.Redirect(Enclosure, Host);

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().BeNull();
    }

    [Theory]
    [InlineData("http://192.168.0.1/admin")]
    [InlineData("http://127.0.0.1:8080/api/admin/users")]
    [InlineData("http://router.lan/admin")]
    public async Task A_hop_onto_the_servers_own_network_is_never_requested(string inside)
    {
        _dns["router.lan"] = [IPAddress.Parse("192.168.0.1")];
        _web.Redirect(Enclosure, inside);
        _web.Serve(inside, HttpStatusCode.OK);

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().BeNull();
        _web.Requests.Should().ContainSingle().Which.RequestUri!.AbsoluteUri.Should().Be(Enclosure);
    }

    [Fact]
    public async Task An_enclosure_on_the_servers_own_network_is_never_requested()
    {
        _dns["nas.lan"] = [IPAddress.Parse("10.0.0.4")];

        var url = await Resolver().ResolveAsync("http://nas.lan/episode.mp3", null, TestContext.Current.CancellationToken);

        url.Should().BeNull();
        _web.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_redirect_off_http_resolves_to_nothing()
    {
        _web.Redirect(Enclosure, "ftp://files.example/ep375.mp3");

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().BeNull();
        _web.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_redirect_loop_gives_up()
    {
        _web.Redirect(Enclosure, Tracker2);
        _web.Redirect(Tracker2, Enclosure);

        var url = await Resolver().ResolveAsync(Enclosure, null, TestContext.Current.CancellationToken);

        url.Should().BeNull();
        _web.Requests.Should().HaveCount(PodcastStreamResolver.MaxRedirects + 1);
    }
}
