using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Tasks;

public class MediaTaskTitleTests
{
    private static readonly TvShow Show = new() { Title = "Black Mirror" };
    private static readonly Season SeasonThree = new() { Title = "Season 3", SeasonNumber = 3, TvShow = Show };

    private static string? Describe(MediaItem item) => MediaTaskTitle.Format(MediaTaskTitle.Projection.Compile()(item));

    [Fact]
    public void A_movie_is_named_by_its_title()
    {
        Describe(new Movie { Title = "Heat" }).Should().Be("Heat");
    }

    [Fact]
    public void An_episode_names_its_show_season_and_number()
    {
        Describe(new Episode { Title = "Nosedive", EpisodeNumber = 1, Season = SeasonThree }).Should().Be("Black Mirror - S03E01 - Nosedive");
    }

    [Fact]
    public void A_season_names_its_show()
    {
        Describe(SeasonThree).Should().Be("Black Mirror - Season 3");
    }

    [Fact]
    public void An_item_that_was_not_found_has_no_name()
    {
        MediaTaskTitle.Format(null).Should().BeNull();
    }
}
