using Vora.Application.Media;

namespace Vora.Application.Tests.Media;

public class SeasonNamesTests
{
    [Theory]
    [InlineData(0, null, "Specials")]
    [InlineData(0, "Season 0", "Specials")]
    [InlineData(0, "season 00", "Specials")]
    [InlineData(0, "Specials", "Specials")]
    [InlineData(0, "Behind the Scenes", "Behind the Scenes")]
    [InlineData(1, null, "Season 1")]
    [InlineData(2, "  ", "Season 2")]
    [InlineData(3, "The Final Season", "The Final Season")]
    public void Season_zero_is_Specials_unless_it_has_a_real_name(int number, string? providerName, string expected)
    {
        SeasonNames.Resolve(number, providerName).Should().Be(expected);
    }

    [Fact]
    public void A_new_season_zero_is_named_Specials()
    {
        SeasonNames.Default(0).Should().Be("Specials");
        SeasonNames.Default(4).Should().Be("Season 4");
    }
}
