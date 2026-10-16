using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Posters.Providers;

namespace Vora.Application.Tests.Posters;

public class BadgeResolverRatingTests
{
    private static readonly string BasePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Overlays");

    [Fact]
    public void The_server_rating_is_drawn_with_the_vora_mark()
    {
        var path = BadgeResolver.GetPlatformLogoPath("Vora", 8m, BasePath);

        path.Should().Be(Path.Combine(BasePath, "Rating", "Vora.png"));
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void A_ratings_provider_named_star_keeps_the_star()
    {
        BadgeResolver.GetPlatformLogoPath("Star", 8m, BasePath).Should().Be(Path.Combine(BasePath, "Rating", "Star.png"));
    }

    [Theory]
    [InlineData("Vora.png")]
    [InlineData("Star.png")]
    public void Both_marks_read_as_a_vora_rating_so_the_score_is_a_whole_number(string fileName)
    {
        BadgeResolver.IsVoraRatingLogo(Path.Combine(BasePath, "Rating", fileName)).Should().BeTrue();
    }

    [Fact]
    public void A_third_party_logo_is_not_read_as_a_vora_rating()
    {
        BadgeResolver.IsVoraRatingLogo(Path.Combine(BasePath, "Rating", "IMDb.png")).Should().BeFalse();
    }

    [Fact]
    public void The_overlay_layout_version_moves_on_so_every_poster_redraws_with_the_new_mark()
    {
        new LocalImageSharpOverlayProvider(NullLogger<LocalImageSharpOverlayProvider>.Instance)
            .LayoutVersion.Should().BeGreaterThanOrEqualTo(10);
    }
}
