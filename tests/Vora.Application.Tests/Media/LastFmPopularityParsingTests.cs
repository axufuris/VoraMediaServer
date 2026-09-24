using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.LastFm;

namespace Vora.Application.Tests.Media;

// Pinned against payloads shaped like Last.fm's documented responses. These
// could not be checked against the live API when this was written — no key was
// configured locally — so the shapes are asserted here, and the first real
// refresh is the thing to watch. A misspelled field is a silent no-op: the value
// simply never arrives, and looks exactly like an artist nobody listens to.
public class LastFmPopularityParsingTests
{
    // Routes by the method= query parameter, so one provider can answer all three
    // calls a refresh makes.
    private sealed class MethodHandler(Dictionary<string, (HttpStatusCode Status, string Body)> byMethod) : HttpMessageHandler
    {
        public List<string> Methods { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            var method = System.Web.HttpUtility.ParseQueryString(query)["method"] ?? string.Empty;
            Methods.Add(method);

            var (status, body) = byMethod.TryGetValue(method, out var r) ? r : (HttpStatusCode.NotFound, "{}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (LastFmListeningDataProvider Provider, MethodHandler Handler) Provider(
        Dictionary<string, (HttpStatusCode, string)> byMethod, string? apiKey = "test-key")
    {
        var handler = new MethodHandler(byMethod);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync(Arg.Any<string>(), "api_key").Returns(apiKey);
        settings.GetSettingAsync(Arg.Any<string>(), "api_secret").Returns((string?)null);

        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IPluginSettingsProvider)).Returns(settings);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(services);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return (new LastFmListeningDataProvider(factory, scopeFactory, NullLogger<LastFmListeningDataProvider>.Instance), handler);
    }

    // artist.getInfo quotes its counts; artist.getTopAlbums does not. Both are
    // real, and trusting either shape alone drops the other's numbers.
    private const string ArtistInfo = """
    {"artist":{"name":"Luke Bryan","stats":{"listeners":"2140000","playcount":"98765432"}}}
    """;

    private const string TopTracks = """
    {"toptracks":{"track":[
        {"name":"Country Girl (Shake It for Me)","playcount":"12000000","listeners":"900000"},
        {"name":"Kansas","playcount":"450000","listeners":"80000"}
    ]}}
    """;

    private const string TopAlbums = """
    {"topalbums":{"album":[
        {"name":"Tailgates & Tanlines","playcount":5400000},
        {"name":"Crash My Party","playcount":4100000}
    ]}}
    """;

    private static Dictionary<string, (HttpStatusCode, string)> Happy() => new()
    {
        ["artist.getInfo"] = (HttpStatusCode.OK, ArtistInfo),
        ["artist.getTopTracks"] = (HttpStatusCode.OK, TopTracks),
        ["artist.getTopAlbums"] = (HttpStatusCode.OK, TopAlbums),
    };

    [Fact]
    public async Task One_refresh_reads_the_artist_its_tracks_and_its_albums_in_three_calls()
    {
        var (provider, handler) = Provider(Happy());

        var result = await provider.GetArtistPopularityAsync("Luke Bryan", 50, 50, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PopularityLookupOutcome.Found);
        handler.Methods.Should().BeEquivalentTo(new[] { "artist.getInfo", "artist.getTopTracks", "artist.getTopAlbums" });
    }

    [Fact]
    public async Task Quoted_counts_are_read()
    {
        var (provider, _) = Provider(Happy());

        var result = await provider.GetArtistPopularityAsync("Luke Bryan", 50, 50, TestContext.Current.CancellationToken);

        result.Listeners.Should().Be(2_140_000);
        result.Plays.Should().Be(98_765_432);
        result.TopTracks.Should().ContainSingle(t => t.Name == "Kansas").Which.Listeners.Should().Be(80_000);
    }

    [Fact]
    public async Task Unquoted_counts_are_read_too()
    {
        var (provider, _) = Provider(Happy());

        var result = await provider.GetArtistPopularityAsync("Luke Bryan", 50, 50, TestContext.Current.CancellationToken);

        result.TopAlbums.Should().ContainSingle(a => a.Name == "Crash My Party").Which.Plays.Should().Be(4_100_000);
    }

    // Last.fm reports an unknown artist as HTTP 200 with an error body, so the
    // status code alone would read it as success.
    [Fact]
    public async Task An_unknown_artist_is_not_found_and_costs_one_call()
    {
        var (provider, handler) = Provider(new()
        {
            ["artist.getInfo"] = (HttpStatusCode.OK, """{"error":6,"message":"The artist you supplied could not be found"}"""),
        });

        var result = await provider.GetArtistPopularityAsync("Nobody", 50, 50, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PopularityLookupOutcome.NotFound);
        handler.Methods.Should().Equal("artist.getInfo");
    }

    [Theory]
    // Rate limited — the artist is fine, the service is not.
    [InlineData(HttpStatusCode.OK, """{"error":29,"message":"Rate limit exceeded"}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "upstream unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    public async Task A_service_problem_is_unavailable_not_not_found(HttpStatusCode status, string body)
    {
        var (provider, _) = Provider(new() { ["artist.getInfo"] = (status, body) });

        var result = await provider.GetArtistPopularityAsync("Luke Bryan", 50, 50, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PopularityLookupOutcome.Unavailable);
    }

    [Fact]
    public async Task No_api_key_is_unavailable_and_makes_no_call()
    {
        var (provider, handler) = Provider(Happy(), apiKey: null);

        var result = await provider.GetArtistPopularityAsync("Luke Bryan", 50, 50, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PopularityLookupOutcome.Unavailable);
        handler.Methods.Should().BeEmpty();
    }

    // Once the artist itself is found, a failure on a follow-up list must not
    // throw away the artist's own figures.
    [Fact]
    public async Task A_failed_track_list_keeps_the_artists_own_figures()
    {
        var (provider, _) = Provider(new()
        {
            ["artist.getInfo"] = (HttpStatusCode.OK, ArtistInfo),
            ["artist.getTopTracks"] = (HttpStatusCode.InternalServerError, "{}"),
            ["artist.getTopAlbums"] = (HttpStatusCode.OK, TopAlbums),
        });

        var result = await provider.GetArtistPopularityAsync("Luke Bryan", 50, 50, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PopularityLookupOutcome.Found);
        result.Listeners.Should().Be(2_140_000);
        result.TopTracks.Should().BeEmpty();
        result.TopAlbums.Should().HaveCount(2);
    }
}
