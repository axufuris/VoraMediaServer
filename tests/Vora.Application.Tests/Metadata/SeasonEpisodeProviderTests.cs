using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Tmdb;
using Vora.Plugins.Providers.Tvdb;

namespace Vora.Application.Tests.Metadata;

public class SeasonEpisodeProviderTests
{
    private sealed class Answers(Func<Uri, string> body) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("A request without an address.");
            Asked.Add(uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body(uri), Encoding.UTF8, "application/json") });
        }
    }

    private static IServiceScopeFactory Settings(params (string Plugin, string Key, string Value)[] values)
    {
        var settings = Substitute.For<IPluginSettingsProvider>();
        foreach (var (plugin, key, value) in values) settings.GetSettingAsync(plugin, key).Returns(value);
        settings.GetMetadataLanguageAsync().Returns("eng");
        return new ServiceCollection().AddSingleton(settings).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private const string TmdbSeason = """
        {
          "episodes": [
            { "id": 63056, "episode_number": 1, "name": "Winter Is Coming", "vote_average": 8.1, "still_path": "/wrJ.jpg",
              "guest_stars": [ { "id": 117642, "name": "Jason Momoa", "character": "Khal Drogo" } ],
              "crew": [ { "id": 44797, "name": "Tim Van Patten", "job": "Director", "department": "Directing" } ] },
            { "id": 63057, "episode_number": 2, "name": "The Kingsroad", "vote_average": 7.8, "still_path": null }
          ]
        }
        """;

    [Fact]
    public async Task TMDB_answers_a_whole_season_and_its_episodes_from_one_request()
    {
        var tmdb = new Answers(_ => TmdbSeason);
        var provider = new TmdbMetadataProvider(new HttpClient(tmdb), Settings(("tmdb_metadata", "api_key", "key")));

        var season = await provider.FetchSeasonEpisodesAsync("1399", 1, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("TMDB answered no season.");
        var second = await provider.FetchEpisodeMetadataAsync("1399", 1, 2, TestContext.Current.CancellationToken);

        tmdb.Asked.Should().ContainSingle().Which.AbsolutePath.Should().EndWith("/tv/1399/season/1");
        season.Keys.Should().Equal(1, 2);
        season[1].BackgroundUrl.Should().Be("https://image.tmdb.org/t/p/w1280/wrJ.jpg");
        season[1].Rating.Should().Be(8.1m);
        season[1].Cast.Select(person => person.Name).Should().Equal("Jason Momoa", "Tim Van Patten");
        second.Should().BeSameAs(season[2]);
        season[2].PosterUrl.Should().BeNull();
    }

    [Fact]
    public async Task TVDB_answers_a_season_from_the_shows_one_episode_list()
    {
        var token = Jwt(DateTimeOffset.UtcNow.AddDays(20));
        var tvdb = new Answers(_ => """
            {
              "data": { "episodes": [
                { "id": 349232, "seasonNumber": 1, "number": 1, "name": "Winter Is Coming", "image": "/banners/episodes/121361/3254641.jpg" },
                { "id": 349233, "seasonNumber": 1, "number": 2, "name": "The Kingsroad", "image": null },
                { "id": 4161693, "seasonNumber": 2, "number": 1, "name": "The North Remembers", "image": "/banners/episodes/121361/4161693.jpg" }
              ] },
              "links": { "next": null }
            }
            """);
        var provider = new TvdbMetadataProvider(new HttpClient(tvdb), Settings((TvdbSession.PluginId, TvdbSession.TokenKey, token)));

        var first = await provider.FetchSeasonEpisodesAsync("121361", 1, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("TVDB answered no season 1.");
        var second = await provider.FetchSeasonEpisodesAsync("121361", 2, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("TVDB answered no season 2.");
        var single = await provider.FetchEpisodeMetadataAsync("121361", 2, 1, TestContext.Current.CancellationToken);

        tvdb.Asked.Should().ContainSingle().Which.AbsolutePath.Should().EndWith("/series/121361/episodes/default/eng");
        first.Keys.Should().Equal(1, 2);
        first[1].PosterUrl.Should().Be("https://artworks.thetvdb.com/banners/episodes/121361/3254641.jpg");
        first[2].PosterUrl.Should().BeNull();
        second.Keys.Should().Equal(1);
        single.Should().BeSameAs(second[1]);
    }

    [Fact]
    public async Task TMDB_ratings_find_an_episode_by_its_TVDB_id_and_never_ask_for_its_own_id_as_a_show()
    {
        var tmdb = new Answers(_ => """{ "tv_results": [ { "vote_average": 1.0 } ], "tv_episode_results": [ { "vote_average": 7.9 } ] }""");
        var provider = new TmdbRatingsProvider(new HttpClient(tmdb), Settings(("tmdb_metadata", "api_key", "key")));

        var score = await provider.FetchRatingAsync(null, "63056", "349232", "Episode", TestContext.Current.CancellationToken);

        score.Should().Be(7.9m);
        tmdb.Asked.Should().ContainSingle().Which.ToString().Should().Contain("find/349232").And.Contain("external_source=tvdb_id");
        tmdb.Asked.Should().NotContain(uri => uri.AbsolutePath.Contains("/tv/"));
    }

    [Fact]
    public void TMDB_ratings_say_their_score_already_comes_with_TMDB_metadata()
    {
        IRatingsProvider tmdbRatings = new TmdbRatingsProvider(new HttpClient(new Answers(_ => "{}")), Settings());
        IRatingsProvider omdb = new Vora.Plugins.Providers.Omdb.OmdbImdbRatingsProvider(new HttpClient(new Answers(_ => "{}")), Settings());

        tmdbRatings.ScoreComesWithMetadataFrom.Should().Be("tmdb_metadata");
        omdb.ScoreComesWithMetadataFrom.Should().BeNull();
    }

    private static string Jwt(DateTimeOffset expiry)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{Encode($"{{\"sub\":\"vora\",\"exp\":{expiry.ToUnixTimeSeconds()}}}")}.signature";
    }
}
