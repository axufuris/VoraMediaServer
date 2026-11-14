using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Tmdb;

namespace Vora.Application.Tests.Discovery;

// Search's "Discovery (not in library)" row: the clients hide the hits the
// server marks as in the library, so TMDB search must send back enough hits
// that a query the library mostly answers (e.g. "marvel") still has some to
// show. It used to send the top four, all of which could be owned.
public class TmdbDiscoverySearchTests
{
    private sealed class OneAnswer(string json) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (TmdbDiscoveryProvider Provider, OneAnswer Handler) NewProvider(object page)
    {
        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync("tmdb_metadata", "api_key").Returns("test-key");
        var services = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();
        var handler = new OneAnswer(JsonSerializer.Serialize(page));
        var provider = new TmdbDiscoveryProvider(
            new HttpClient(handler),
            services.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()));
        return (provider, handler);
    }

    // TMDB's multi search: a page of up to 20 movies, shows and people.
    private static object Page(int movies, int shows, int people) => new
    {
        results = Enumerable.Range(1, movies).Select(i => (object)new { media_type = "movie", id = i, title = $"Movie {i}", release_date = "2019-03-08", poster_path = "/p.jpg" })
            .Concat(Enumerable.Range(1, shows).Select(i => (object)new { media_type = "tv", id = 100 + i, name = $"Show {i}", first_air_date = "2013-09-24", poster_path = (string?)null }))
            .Concat(Enumerable.Range(1, people).Select(i => (object)new { media_type = "person", id = 200 + i, name = $"Person {i}" }))
            .ToList(),
    };

    [Fact]
    public async Task Search_returns_the_whole_first_page_of_movies_and_shows_not_just_four()
    {
        var (provider, handler) = NewProvider(Page(movies: 12, shows: 6, people: 2));

        var hits = (await provider.SearchAsync("marvel")).ToList();

        hits.Should().HaveCount(18);
        hits.Should().OnlyContain(h => h.Type == "Movie" || h.Type == "TvShow");
        hits.Select(h => h.Title).Should().Contain(["Movie 12", "Show 6"]);
        hits.First(h => h.Title == "Show 1").ExternalId.Should().Be("101");
        handler.Asked!.Query.Should().Contain("query=marvel").And.Contain("page=1");
    }

    [Fact]
    public async Task Search_sends_back_at_most_a_page()
    {
        var (provider, _) = NewProvider(Page(movies: 25, shows: 0, people: 0));

        var hits = await provider.SearchAsync("star");

        hits.Should().HaveCount(TmdbDiscoveryProvider.SearchResultLimit);
    }
}
