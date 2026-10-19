using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Media;

public class ExtraNamesTests
{
    [Theory]
    [InlineData("The.Movie.2007.1080p")]
    [InlineData("The.Movie.2007.1080p.BluRay.x264")]
    [InlineData("The Movie (2007)")]
    [InlineData("The Movie")]
    [InlineData("the.movie.720p")]
    [InlineData("")]
    [InlineData("Trailer")]
    [InlineData("trailers")]
    public void A_trailer_named_after_its_film_or_named_nothing_reads_as_the_films_trailer(string stored)
    {
        ExtraNames.Display(stored, MediaExtraType.Trailer, "The Movie").Should().Be("The Movie Trailer");
    }

    [Fact]
    public void The_films_own_release_name_reads_as_its_trailer_even_when_its_metadata_title_is_longer()
    {
        ExtraNames.Display("The.Fellowship.of.the.Ring.2001.1080p", MediaExtraType.Trailer, "The Lord of the Rings: The Fellowship of the Ring")
            .Should().Be("The Lord of the Rings: The Fellowship of the Ring Trailer");
    }

    [Theory]
    [InlineData("Official Trailer 2", "Official Trailer 2")]
    [InlineData("Teaser.Trailer", "Teaser Trailer")]
    [InlineData("Making.of.the.Dragon.1080p", "Making of the Dragon")]
    [InlineData("Ending_Alternate", "Ending Alternate")]
    public void A_name_of_its_own_is_kept_and_made_readable(string stored, string expected)
    {
        ExtraNames.Display(stored, MediaExtraType.Featurette, "The Movie").Should().Be(expected);
    }

    [Theory]
    [InlineData(MediaExtraType.Featurette, "The Movie Featurette")]
    [InlineData(MediaExtraType.BehindTheScenes, "The Movie Behind the Scenes")]
    [InlineData(MediaExtraType.DeletedScene, "The Movie Deleted Scene")]
    [InlineData(MediaExtraType.Other, "The Movie Extra")]
    public void Every_kind_of_extra_named_after_its_film_gets_its_own_label(MediaExtraType type, string expected)
    {
        ExtraNames.Display("The.Movie.2007.1080p", type, "The Movie").Should().Be(expected);
    }

    [Fact]
    public void Without_a_parent_title_the_label_stands_alone()
    {
        ExtraNames.Display("", MediaExtraType.Trailer, null).Should().Be("Trailer");
    }

    [Fact]
    public void A_details_page_names_each_of_its_extras_after_the_title()
    {
        var details = new MediaDetailsVM
        {
            Title = "Severance",
            Extras =
            [
                new MediaExtraVM { Title = "Severance.S01.Trailer.1080p", ExtraType = MediaExtraType.Trailer },
                new MediaExtraVM { Title = "Inside the Lumon Office", ExtraType = MediaExtraType.BehindTheScenes },
            ]
        };

        ExtraNames.Apply(details);

        details.Extras.Select(extra => extra.Title).Should().Equal("Severance Trailer", "Inside the Lumon Office");
    }
}
