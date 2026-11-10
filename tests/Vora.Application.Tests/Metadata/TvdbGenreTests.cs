using System.Text.Json;
using Vora.Plugins.Providers.Tmdb;
using Vora.Plugins.Providers.Tvdb;

namespace Vora.Application.Tests.Metadata;

// TVDB's genre ids were stored as if they were TMDB's. Its 12 is Drama and
// TMDB's 12 is Adventure, so Breaking Bad (Drama, Crime) became "Adventure,
// Fantasy", and Comedy (TVDB 15, no TMDB genre there) was dropped from every
// show. The ids and names below are TVDB's own, from its /genres list.
public class TvdbGenreTests
{
    private static JsonElement Series(string genres) =>
        JsonDocument.Parse($$"""{ "name": "Show", "genres": {{genres}} }""").RootElement;

    [Fact]
    public void Genres_are_read_by_name_not_by_tvdbs_own_ids()
    {
        var breakingBad = Series("""[{ "id": 12, "name": "Drama", "slug": "drama" }, { "id": 14, "name": "Crime", "slug": "crime" }]""");
        var himym = Series("""[{ "id": 15, "name": "Comedy", "slug": "comedy" }, { "id": 28, "name": "Romance", "slug": "romance" }]""");

        TvdbMetadataProvider.GenreIds(breakingBad).Should().Equal(18, 80);
        TvdbMetadataProvider.GenreIds(himym).Should().Equal(35, 10749);
    }

    [Fact]
    public void Tvdbs_own_names_find_the_tmdb_genre_and_formats_with_none_are_left_out()
    {
        var show = Series("""
            [{ "id": 2, "name": "Science Fiction" }, { "id": 16, "name": "Children" }, { "id": 23, "name": "Talk Show" },
             { "id": 22, "name": "Suspense" }, { "id": 27, "name": "Anime" }, { "id": 5, "name": "Mini-Series" }, { "id": 21, "name": "Sport" }]
            """);

        TvdbMetadataProvider.GenreIds(show).Should().Equal(878, 10762, 10767, 53, 16);
    }

    [Fact]
    public void A_series_without_genres_has_none()
    {
        TvdbMetadataProvider.GenreIds(JsonDocument.Parse("""{ "name": "Show" }""").RootElement).Should().BeEmpty();
        TvdbMetadataProvider.GenreIds(Series("null")).Should().BeEmpty();
        TvdbMetadataProvider.GenreIds(Series("""[{ "id": 12 }, "Drama"]""")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Drama", 18)]
    [InlineData("  comedy ", 35)]
    [InlineData("Sci-Fi & Fantasy", 10765)]
    [InlineData("Game Show", 10764)]
    public void A_name_finds_its_tmdb_id(string name, int id) =>
        TmdbGenres.IdFor(name).Should().Be(id);

    [Fact]
    public void Repeats_and_unknown_names_are_dropped()
    {
        TmdbGenres.IdsFor(new[] { "Drama", "drama", "Podcast", null, "", "Crime" }).Should().Equal(18, 80);
    }
}
