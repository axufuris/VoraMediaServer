using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Deezer;

namespace Vora.Application.Tests.Media;

// Pinned against payloads captured from the live Deezer API. Checked live: an
// ISRC lookup returns the track with explicit_lyrics and explicit_content_lyrics;
// an unknown ISRC is HTTP 200 with error code 800; the clean edition of an album
// codes its tracks 3 ("edited") with explicit_lyrics false.
public class DeezerContentRatingParsingTests
{
    private sealed class PathHandler(Func<string, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            var (status, body) = answer(path);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (DeezerContentRatingProvider Provider, PathHandler Handler) Provider(Func<string, (HttpStatusCode, string)> answer)
    {
        var handler = new PathHandler(answer);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return (new DeezerContentRatingProvider(factory, NullLogger<DeezerContentRatingProvider>.Instance), handler);
    }

    private const string ExplicitTrack = """
    {"id":916424,"title":"Without Me","isrc":"USIR10211038","duration":290,"explicit_lyrics":true,"explicit_content_lyrics":1}
    """;

    private const string NoData = """{"error":{"type":"DataException","message":"no data","code":800}}""";
    private const string Quota = """{"error":{"type":"Exception","message":"Quota limit exceeded","code":4}}""";

    [Fact]
    public async Task An_isrc_lookup_reads_the_explicit_flag()
    {
        var (provider, handler) = Provider(_ => (HttpStatusCode.OK, ExplicitTrack));

        var result = await provider.GetTrackAdvisoryByIsrcAsync("USIR10211038", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ContentRatingLookupOutcome.Found);
        result.Advisory.Should().Be(ProviderAdvisory.Explicit);
        handler.Paths.Should().Equal("/track/isrc:USIR10211038");
    }

    [Fact]
    public async Task An_unknown_isrc_is_not_found_rather_than_unavailable()
    {
        var (provider, _) = Provider(_ => (HttpStatusCode.OK, NoData));

        var result = await provider.GetTrackAdvisoryByIsrcAsync("ZZZZ00000000", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ContentRatingLookupOutcome.NotFound);
    }

    // A spent quota also arrives as HTTP 200 — reading it as "not found" would
    // stamp the rest of the library checked with nothing learned.
    [Fact]
    public async Task A_spent_quota_is_unavailable()
    {
        var (provider, _) = Provider(_ => (HttpStatusCode.OK, Quota));

        var result = await provider.GetTrackAdvisoryByIsrcAsync("USIR10211038", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ContentRatingLookupOutcome.Unavailable);
    }

    [Theory]
    [InlineData("""{"explicit_lyrics":true,"explicit_content_lyrics":1}""", ProviderAdvisory.Explicit)]
    [InlineData("""{"explicit_lyrics":false,"explicit_content_lyrics":0}""", ProviderAdvisory.Clean)]
    [InlineData("""{"explicit_lyrics":false,"explicit_content_lyrics":3}""", ProviderAdvisory.Clean)]
    [InlineData("""{"explicit_lyrics":false,"explicit_content_lyrics":2}""", ProviderAdvisory.Unknown)]
    [InlineData("""{"explicit_lyrics":false,"explicit_content_lyrics":6}""", ProviderAdvisory.Unknown)]
    [InlineData("""{"explicit_lyrics":false}""", ProviderAdvisory.Unknown)]
    public void Advisory_codes_map_to_ratings(string json, ProviderAdvisory expected)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        DeezerContentRatingProvider.ReadAdvisory(doc.RootElement).Should().Be(expected);
    }

    [Fact]
    public async Task Album_editions_come_back_with_every_tracks_flag()
    {
        const string search = """
        {"data":[
          {"id":103248,"title":"The Eminem Show","artist":{"name":"Eminem"}},
          {"id":14638278,"title":"The Eminem Show","artist":{"name":"Eminem"}}
        ]}
        """;
        const string explicitTracks = """
        {"data":[{"title":"White America","isrc":"USIR10211052","duration":324,"explicit_lyrics":true,"explicit_content_lyrics":1}],"total":1}
        """;
        const string cleanTracks = """
        {"data":[{"title":"White America","isrc":"USIR10211126","duration":324,"explicit_lyrics":false,"explicit_content_lyrics":3}],"total":1}
        """;
        var (provider, _) = Provider(path =>
            path.StartsWith("/search/album") ? (HttpStatusCode.OK, search)
            : path.StartsWith("/album/103248/") ? (HttpStatusCode.OK, explicitTracks)
            : (HttpStatusCode.OK, cleanTracks));

        var result = await provider.GetAlbumEditionsAsync("Eminem", "The Eminem Show", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ContentRatingLookupOutcome.Found);
        result.Editions.Select(e => e.Tracks.Single().Advisory)
            .Should().Equal(ProviderAdvisory.Explicit, ProviderAdvisory.Clean);
        result.Editions[1].Tracks.Single().Isrc.Should().Be("USIR10211126");
    }

    [Fact]
    public async Task An_album_search_with_no_results_is_not_found()
    {
        var (provider, _) = Provider(_ => (HttpStatusCode.OK, """{"data":[],"total":0}"""));

        var result = await provider.GetAlbumEditionsAsync("Nobody", "Nothing", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ContentRatingLookupOutcome.NotFound);
    }

    [Fact]
    public async Task A_failed_tracklist_makes_the_whole_album_unavailable()
    {
        var (provider, _) = Provider(path => path.StartsWith("/search/album")
            ? (HttpStatusCode.OK, """{"data":[{"id":1,"title":"X","artist":{"name":"Y"}}]}""")
            : (HttpStatusCode.ServiceUnavailable, "{}"));

        var result = await provider.GetAlbumEditionsAsync("Y", "X", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ContentRatingLookupOutcome.Unavailable);
    }
}
