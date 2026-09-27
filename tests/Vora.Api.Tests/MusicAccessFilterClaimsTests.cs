using System.Security.Claims;
using Vora.Api.Extensions;

namespace Vora.Api.Tests;

// Whether music is restricted has to follow from the MUSIC allowlist alone. The
// profile's hasAllRatings claim is true only when movies, TV and music are all
// unrestricted, and the music endpoints used to pass it straight through — so a
// parent who restricted a child's films and left music open saw the child's
// music quietly cut down to untagged tracks, with every Clean and Explicit one
// gone.
public class MusicAccessFilterClaimsTests
{
    private static ClaimsPrincipal Profile(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));

    [Fact]
    public void Restricting_movies_does_not_restrict_music()
    {
        var child = Profile(
            ("hasAllRatings", "false"),
            ("allowedMovieRating", "G"),
            ("allowedMovieRating", "PG"));

        var filter = child.GetMusicAccessFilter();

        filter.HasAllRatings.Should().BeTrue("no music rating was restricted");
        filter.AllowedRatings.Should().BeEmpty();
    }

    [Fact]
    public void Restricting_tv_does_not_restrict_music()
    {
        var child = Profile(("hasAllRatings", "false"), ("allowedTvRating", "TV-Y"));

        child.GetMusicAccessFilter().HasAllRatings.Should().BeTrue();
    }

    [Fact]
    public void A_music_allowlist_restricts_music()
    {
        var child = Profile(("hasAllRatings", "false"), ("allowedMusicRating", "Clean"));

        var filter = child.GetMusicAccessFilter();

        filter.HasAllRatings.Should().BeFalse();
        filter.AllowedRatings.Should().Equal("Clean");
    }

    [Fact]
    public void An_unrestricted_profile_is_unrestricted()
    {
        var adult = Profile(("hasAllRatings", "true"));

        var filter = adult.GetMusicAccessFilter();

        filter.HasAllRatings.Should().BeTrue();
        filter.BlockUnratedContent.Should().BeFalse();
    }

    [Fact]
    public void Block_unrated_is_carried_through()
    {
        var child = Profile(("blockUnrated", "true"));

        child.GetMusicAccessFilter().BlockUnratedContent.Should().BeTrue();
    }
}
