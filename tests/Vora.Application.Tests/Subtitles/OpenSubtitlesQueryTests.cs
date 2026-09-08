using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.OpenSubtitles;

namespace Vora.Application.Tests.Subtitles;

// The query the provider builds decides which film's subtitles come back. An
// external id pins the exact title; a bare title match is where wrong-film
// subtitles come from, so the id has to win whenever there is one.
public class OpenSubtitlesQueryTests
{
    private static string Url(SubtitleSearchQuery query, params string[] languages) =>
        OpenSubtitlesSubtitleProvider.BuildSearchUrl(query, languages);

    [Fact]
    public void An_imdb_id_is_preferred_over_the_title()
    {
        var url = Url(new SubtitleSearchQuery { Title = "The Odyssey", Year = 2026, ImdbId = "tt1368337", TmdbId = "1368337" }, "en");

        url.Should().Contain("imdb_id=1368337");
        url.Should().NotContain("query=");
        url.Should().NotContain("tmdb_id=");
    }

    // OpenSubtitles wants the numeric id, not the tt-prefixed one.
    [Fact]
    public void The_imdb_prefix_and_padding_are_stripped()
    {
        Url(new SubtitleSearchQuery { ImdbId = "tt0083658" }, "en").Should().Contain("imdb_id=83658");
    }

    [Fact]
    public void A_tmdb_id_is_used_when_there_is_no_imdb_id()
    {
        var url = Url(new SubtitleSearchQuery { Title = "The Odyssey", TmdbId = "1368337" }, "en");

        url.Should().Contain("tmdb_id=1368337");
        url.Should().NotContain("query=");
    }

    [Fact]
    public void A_title_and_year_are_the_last_resort()
    {
        var url = Url(new SubtitleSearchQuery { Title = "The Odyssey", Year = 2026 }, "en");

        url.Should().Contain("query=The%20Odyssey");
        url.Should().Contain("year=2026");
    }

    [Fact]
    public void An_episode_carries_its_season_and_episode_numbers()
    {
        var url = Url(new SubtitleSearchQuery { ImdbId = "tt14500874", Season = 2, Episode = 5 }, "en");

        url.Should().Contain("season_number=2");
        url.Should().Contain("episode_number=5");
    }

    [Fact]
    public void A_movie_carries_no_numbering()
    {
        var url = Url(new SubtitleSearchQuery { ImdbId = "tt1368337" }, "en");

        url.Should().NotContain("season_number");
        url.Should().NotContain("episode_number");
    }

    [Fact]
    public void Languages_are_joined_and_lowercased()
    {
        Url(new SubtitleSearchQuery { ImdbId = "tt1" }, "EN", "es").Should().Contain("languages=en%2Ces");
    }

    // A search with no language would return every language the source has,
    // which is thousands of rows for a popular film.
    [Fact]
    public void Configured_languages_fill_in_when_a_request_names_none()
    {
        OpenSubtitlesSubtitleProvider.ParseLanguages("en, es , EN").Should().BeEquivalentTo(["en", "es"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void English_is_the_fallback_when_nothing_is_configured(string? configured)
    {
        OpenSubtitlesSubtitleProvider.ParseLanguages(configured).Should().BeEquivalentTo(["en"]);
    }

    [Theory]
    [InlineData("Movie.srt", "srt")]
    [InlineData("Movie.ASS", "ass")]
    [InlineData("Movie.vtt", "vtt")]
    // Anything unrecognised is treated as SubRip, which is what the vast
    // majority of the source's files are.
    [InlineData("Movie.zip", "srt")]
    [InlineData(null, "srt")]
    public void The_format_comes_from_the_file_name(string? fileName, string expected)
    {
        OpenSubtitlesSubtitleProvider.NormalizeFormat(fileName).Should().Be(expected);
    }
}
