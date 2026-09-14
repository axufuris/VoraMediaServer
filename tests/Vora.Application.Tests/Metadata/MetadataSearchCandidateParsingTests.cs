using System.Text.Json;
using Vora.Plugins.Providers.Tmdb;
using Vora.Plugins.Providers.Tvdb;

namespace Vora.Application.Tests.Metadata;

public class MetadataSearchCandidateParsingTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void A_tmdb_show_search_yields_candidates_with_year_and_poster()
    {
        var root = Json("""
        {
          "results": [
            { "id": 194583, "name": "The Walking Dead: Dead City", "first_air_date": "2023-06-18", "overview": "Maggie and Negan travel to Manhattan.", "poster_path": "/abc.jpg" },
            { "id": 257679, "name": "The Last Drive-in: The Walking Dead - Dead City", "first_air_date": "2023-06-18", "overview": "", "poster_path": null }
          ]
        }
        """);

        var candidates = TmdbMetadataProvider.ParseSearchCandidates(root, "name", "first_air_date");

        candidates.Should().HaveCount(2);
        candidates[0].Should().BeEquivalentTo(new
        {
            Source = "tmdb",
            ExternalId = "194583",
            Title = "The Walking Dead: Dead City",
            Year = 2023,
            Overview = "Maggie and Negan travel to Manhattan.",
            PosterUrl = "https://image.tmdb.org/t/p/w342/abc.jpg"
        });
        candidates[1].PosterUrl.Should().BeNull();
    }

    [Fact]
    public void A_tmdb_movie_search_reads_the_movie_title_and_release_date()
    {
        var root = Json("""{ "results": [ { "id": 329865, "title": "Arrival", "release_date": "2016-11-10" } ] }""");

        var candidate = TmdbMetadataProvider.ParseSearchCandidates(root, "title", "release_date").Single();

        candidate.Title.Should().Be("Arrival");
        candidate.Year.Should().Be(2016);
    }

    [Theory]
    [InlineData("""{ "results": [ { "id": 1, "name": "No Date" } ] }""")]
    [InlineData("""{ "results": [ { "id": 1, "name": "Blank Date", "first_air_date": "" } ] }""")]
    public void A_missing_or_blank_date_leaves_the_year_empty(string json)
    {
        TmdbMetadataProvider.ParseSearchCandidates(Json(json), "name", "first_air_date").Single().Year.Should().BeNull();
    }

    [Fact]
    public void A_tmdb_result_without_a_title_or_id_is_skipped()
    {
        var root = Json("""{ "results": [ { "id": 1 }, { "name": "No id" }, { "id": 2, "name": "Kept" } ] }""");

        TmdbMetadataProvider.ParseSearchCandidates(root, "name", "first_air_date").Should().ContainSingle().Which.Title.Should().Be("Kept");
    }

    [Fact]
    public void A_tmdb_search_is_capped()
    {
        var results = string.Join(",", Enumerable.Range(1, 25).Select(i => "{ \"id\": " + i + ", \"name\": \"Show " + i + "\" }"));

        TmdbMetadataProvider.ParseSearchCandidates(Json("{ \"results\": [" + results + "] }"), "name", "first_air_date")
            .Should().HaveCount(TmdbMetadataProvider.MaxSearchCandidates);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "results": null }""")]
    [InlineData("""{ "status_message": "Invalid API key" }""")]
    public void A_tmdb_error_body_yields_no_candidates(string json)
    {
        TmdbMetadataProvider.ParseSearchCandidates(Json(json), "name", "first_air_date").Should().BeEmpty();
    }

    [Fact]
    public void A_tvdb_search_yields_candidates()
    {
        var root = Json("""
        {
          "status": "success",
          "data": [
            { "tvdb_id": "417549", "name": "The Walking Dead: Dead City", "year": "2023", "overview": "Maggie and Negan.", "image_url": "https://artworks.thetvdb.com/banners/v4/series/417549/posters/abc.jpg", "type": "series" }
          ]
        }
        """);

        TvdbMetadataProvider.ParseSearchCandidates(root).Single().Should().BeEquivalentTo(new
        {
            Source = "tvdb",
            ExternalId = "417549",
            Title = "The Walking Dead: Dead City",
            Year = 2023,
            Overview = "Maggie and Negan.",
            PosterUrl = "https://artworks.thetvdb.com/banners/v4/series/417549/posters/abc.jpg"
        });
    }

    [Fact]
    public void A_tvdb_result_with_no_year_or_image_still_counts()
    {
        var root = Json("""{ "data": [ { "tvdb_id": "1", "name": "Obscure", "year": null, "image_url": null } ] }""");

        var candidate = TvdbMetadataProvider.ParseSearchCandidates(root).Single();

        candidate.Year.Should().BeNull();
        candidate.PosterUrl.Should().BeNull();
    }

    [Fact]
    public void A_tvdb_result_without_an_id_is_skipped()
    {
        var root = Json("""{ "data": [ { "name": "No id" }, { "tvdb_id": "2", "name": "Kept" } ] }""");

        TvdbMetadataProvider.ParseSearchCandidates(root).Should().ContainSingle().Which.ExternalId.Should().Be("2");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "status": "failure", "message": "Unauthorized" }""")]
    public void A_tvdb_error_body_yields_no_candidates(string json)
    {
        TvdbMetadataProvider.ParseSearchCandidates(Json(json)).Should().BeEmpty();
    }
}
