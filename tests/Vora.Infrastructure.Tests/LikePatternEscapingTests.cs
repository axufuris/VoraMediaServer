using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// The album search builds an ILIKE pattern by wrapping the term in %. Anything
// the user types that LIKE reads as syntax has to stop meaning that first, or
// searching for "50%" returns the library and searching for "_" returns
// everything with at least one character in the right place.
//
// Tested directly because the query itself cannot be: ILike is a Postgres
// function with no in-memory translation, so the suite can reach the pattern but
// never the match.
public class LikePatternEscapingTests
{
    [Theory]
    [InlineData("zeppelin", "zeppelin")]
    [InlineData("50%", @"50\%")]
    [InlineData("_", @"\_")]
    [InlineData("100%_pure", @"100\%\_pure")]
    // The escape character itself. It goes first in the implementation, so the
    // backslashes it adds for % and _ are not escaped a second time.
    [InlineData(@"AC\DC", @"AC\\DC")]
    [InlineData(@"\%", @"\\\%")]
    public void Like_syntax_in_a_term_is_escaped(string term, string expected) =>
        MusicRepository.EscapeLikePattern(term).Should().Be(expected);

    [Fact]
    public void An_ordinary_term_is_left_alone()
    {
        foreach (var term in new[] { "Led Zeppelin", "Sgt. Pepper's", "Blink-182", "Sigur Rós", "?", "*" })
        {
            MusicRepository.EscapeLikePattern(term).Should().Be(term);
        }
    }

    // A term of only wildcards must not collapse to a pattern that matches
    // everything — which is what would happen if escaping were skipped.
    [Fact]
    public void A_term_of_nothing_but_wildcards_still_escapes()
    {
        MusicRepository.EscapeLikePattern("%%%").Should().Be(@"\%\%\%");
        MusicRepository.EscapeLikePattern("___").Should().Be(@"\_\_\_");
    }
}
