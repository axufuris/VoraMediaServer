using System.Reflection;
using Vora.Application.Ai;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Ai;

// The audit prompt is the last thing standing between a wrong story year and
// the stored order, so its two load-bearing instructions are pinned here.
//
// It previously asked whether an item looked right "relative to its
// neighbours", and let the model omit anything it considered fine. Both let the
// same failure through: a title given its release year by mistake sits among
// other titles of that release year, reads as locally consistent, and is
// confirmed by silence. Black Panther — set 2016, released 2018 — landed 13
// places late in a real collection that way.
public class ChronologyPromptTests
{
    private static string BuildAuditPrompt(params (int Index, string Title, double SetYear)[] entries)
    {
        var ordered = entries
            .Select(e => new CollectionOrderingItemDto { Index = e.Index, Title = e.Title, MediaType = "Movie" })
            .ToList();
        var setYears = entries.ToDictionary(e => e.Index, e => e.SetYear);
        var review = entries.Select(e => e.Index).ToHashSet();

        var method = typeof(OpenAiChronologyProvider)
            .GetMethod("BuildVerificationPrompt", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [ "MCU in story order", ordered, setYears, review ])!;
    }

    private static string AuditPrompt() => BuildAuditPrompt(
        (0, "Captain America: Civil War", 2016.05),
        (1, "Avengers: Infinity War", 2018.05),
        (2, "Black Panther", 2018.25));

    [Fact]
    public void Audit_asks_for_the_story_year_on_its_own_merits()
    {
        var prompt = AuditPrompt();

        prompt.Should().Contain("PRIMARILY takes place");
        prompt.Should().Contain("on its own merits FIRST");
    }

    [Fact]
    public void Audit_warns_against_judging_by_neighbours()
    {
        AuditPrompt().Should().Contain("Do NOT judge it by whether it looks plausible next to its current neighbours");
    }

    [Fact]
    public void Audit_names_the_release_year_trap()
    {
        var prompt = AuditPrompt();

        prompt.Should().Contain("release year by mistake");
        prompt.Should().Contain("set one or two years earlier");
    }

    [Fact]
    public void Audit_requires_an_answer_for_every_reviewed_index()
    {
        var prompt = AuditPrompt();

        prompt.Should().Contain("EVERY index listed for audit");
        prompt.Should().Contain("Never omit a reviewed index");
        // The old wording invited silence, which made "no change" the cheap answer.
        prompt.Should().NotContain("omit it");
        prompt.Should().NotContain("already correct");
    }

    [Fact]
    public void Audit_lists_the_current_order_with_every_set_year()
    {
        var prompt = AuditPrompt();

        prompt.Should().Contain("Black Panther");
        prompt.Should().Contain("2018.25");
        prompt.Should().Contain("Avengers: Infinity War");
        prompt.Should().Contain("Audit these indices: 0, 1, 2");
    }

    [Fact]
    public void Scoring_warns_that_a_modern_entry_can_still_be_set_earlier()
    {
        var method = typeof(OpenAiChronologyProvider)
            .GetMethod("BuildScoringPrompt", BindingFlags.NonPublic | BindingFlags.Static)!;
        var batch = new List<CollectionOrderingItemDto>
        {
            new() { Index = 0, Title = "Black Panther", Year = 2018, MediaType = "Movie" },
        };

        var prompt = (string)method.Invoke(null, [ "MCU in story order", batch ])!;

        prompt.Should().Contain("Never fall back on the release year for a modern-era entry");
        prompt.Should().Contain("picking up days after an earlier film");
    }
}
