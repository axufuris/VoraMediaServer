using Vora.Application.Metadata;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Metadata;

public class MediaMatchIdsTests
{
    [Theory]
    [InlineData("tmdb", "tmdb")]
    [InlineData("TVDB", "tvdb")]
    [InlineData(" Imdb ", "imdb")]
    public void A_known_source_is_normalized(string input, string expected)
    {
        MediaMatchIds.NormalizeSource(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("trakt")]
    public void An_unknown_source_is_rejected(string? input)
    {
        MediaMatchIds.NormalizeSource(input).Should().BeNull();
    }

    [Theory]
    [InlineData("tt18546730", "tt18546730")]
    [InlineData("TT18546730", "tt18546730")]
    [InlineData("https://www.imdb.com/title/tt18546730/", "tt18546730")]
    [InlineData("  tt0944947 ", "tt0944947")]
    public void An_imdb_id_or_link_yields_the_id(string input, string expected)
    {
        MediaMatchIds.NormalizeId(MediaMatchIds.Imdb, input).Should().Be(expected);
    }

    [Theory]
    [InlineData("18546730")]
    [InlineData("tt12")]
    [InlineData("the walking dead")]
    [InlineData("")]
    public void Something_that_is_not_an_imdb_id_is_rejected(string input)
    {
        MediaMatchIds.NormalizeId(MediaMatchIds.Imdb, input).Should().BeNull();
    }

    [Theory]
    [InlineData("194583", "194583")]
    [InlineData("https://www.themoviedb.org/tv/194583-the-walking-dead-dead-city", "194583")]
    [InlineData("https://www.themoviedb.org/movie/603-the-matrix", "603")]
    public void A_tmdb_id_or_link_yields_the_id(string input, string expected)
    {
        MediaMatchIds.NormalizeId(MediaMatchIds.Tmdb, input).Should().Be(expected);
    }

    [Theory]
    [InlineData("417549", "417549")]
    public void A_tvdb_id_is_accepted(string input, string expected)
    {
        MediaMatchIds.NormalizeId(MediaMatchIds.Tvdb, input).Should().Be(expected);
    }

    [Theory]
    [InlineData(MediaMatchIds.Tvdb, "the-walking-dead-dead-city")]
    [InlineData(MediaMatchIds.Tmdb, "tt18546730")]
    [InlineData(MediaMatchIds.Tvdb, "-1")]
    public void An_id_in_the_wrong_shape_for_its_source_is_rejected(string source, string input)
    {
        MediaMatchIds.NormalizeId(source, input).Should().BeNull();
    }

    [Theory]
    [InlineData("tt18546730")]
    [InlineData("https://www.imdb.com/title/tt18546730/?ref_=nv_sr_srsg_0")]
    public void A_pasted_imdb_id_is_looked_up_directly(string query)
    {
        MediaMatchIds.FromQuery(query).Should().Be(new MediaMatchIdQuery(MediaMatchIds.Imdb, "tt18546730", null));
    }

    [Fact]
    public void A_pasted_tmdb_show_link_says_it_is_a_show()
    {
        MediaMatchIds.FromQuery("https://www.themoviedb.org/tv/194583-the-walking-dead-dead-city")
            .Should().Be(new MediaMatchIdQuery(MediaMatchIds.Tmdb, "194583", true));
    }

    [Fact]
    public void A_pasted_tmdb_movie_link_says_it_is_a_movie()
    {
        MediaMatchIds.FromQuery("https://www.themoviedb.org/movie/603-the-matrix")
            .Should().Be(new MediaMatchIdQuery(MediaMatchIds.Tmdb, "603", false));
    }

    [Theory]
    [InlineData("The Walking Dead - Dead City")]
    [InlineData("Se7en")]
    [InlineData("Blade Runner 2049")]
    [InlineData("")]
    [InlineData(null)]
    public void A_title_is_searched_as_text(string? query)
    {
        MediaMatchIds.FromQuery(query).Should().BeNull();
    }

    [Theory]
    [InlineData("The Walking Dead - Dead City [imdb-]", "The Walking Dead - Dead City")]
    [InlineData("The Walking Dead - Dead City [imdb-tt18546730]", "The Walking Dead - Dead City")]
    [InlineData("Dune (2021) {tmdb-438631}", "Dune")]
    [InlineData("Severance [tvdbid-371980]", "Severance")]
    [InlineData("Arrival (2016)", "Arrival")]
    [InlineData("Blade Runner 2049", "Blade Runner 2049")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void A_title_is_cleaned_of_id_tags_and_a_trailing_year(string? title, string expected)
    {
        MediaMatchIds.CleanSearchTitle(title).Should().Be(expected);
    }

    [Theory]
    [InlineData(MediaMatchIds.Tmdb, "194583")]
    [InlineData(MediaMatchIds.Tvdb, "417549")]
    [InlineData(MediaMatchIds.Imdb, "tt18546730")]
    public void Assigning_sets_only_the_matching_id(string source, string id)
    {
        var show = new TvShow { Title = "The Walking Dead: Dead City" };

        MediaMatchIds.Assign(show, source, id);

        show.TmdbId.Should().Be(source == MediaMatchIds.Tmdb ? id : null);
        show.TvdbId.Should().Be(source == MediaMatchIds.Tvdb ? id : null);
        show.ImdbId.Should().Be(source == MediaMatchIds.Imdb ? id : null);
    }

    [Fact]
    public void Assigning_an_unknown_source_throws()
    {
        var act = () => MediaMatchIds.Assign(new Movie { Title = "Arrival" }, "trakt", "1");

        act.Should().Throw<ArgumentException>();
    }
}
