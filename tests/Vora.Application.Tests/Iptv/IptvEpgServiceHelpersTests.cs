using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;

namespace Vora.Application.Tests.Iptv;

public class IptvEpgServiceHelpersTests
{
    private static IptvProgramDto MakeProgram(string id, string channelId, DateTime start, string rating = "NR", string title = "Show", string description = "desc") =>
        new()
        {
            Id = id,
            ChannelId = channelId,
            Title = title,
            Description = description,
            StartTime = start,
            EndTime = start.AddMinutes(30),
            ContentRating = rating
        };

    [Fact]
    public void ClaimChannelsForSource_first_source_wins_ownership_per_channel()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var t0 = new DateTime(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

        var parsedA = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ch1"] = new() { MakeProgram("a1", "ch1", t0) }
        };
        var parsedB = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ch1"] = new() { MakeProgram("b1", "ch1", t0.AddHours(1)) }
        };

        var merged = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
        var claimedBy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        IptvEpgService.ClaimChannelsForSource(sourceA, parsedA, merged, claimedBy);
        IptvEpgService.ClaimChannelsForSource(sourceB, parsedB, merged, claimedBy);

        claimedBy["ch1"].Should().Be(sourceA);
        merged["ch1"].Should().HaveCount(1);
        merged["ch1"][0].Id.Should().Be("a1");
    }

    [Fact]
    public void ClaimChannelsForSource_skips_channels_with_zero_programs()
    {
        var sourceA = Guid.NewGuid();
        var parsed = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["empty_ch"] = new List<IptvProgramDto>()
        };

        var merged = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
        var claimedBy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        IptvEpgService.ClaimChannelsForSource(sourceA, parsed, merged, claimedBy);

        claimedBy.Should().NotContainKey("empty_ch");
        merged.Should().NotContainKey("empty_ch");
    }

    [Fact]
    public void ClaimChannelsForSource_dedupes_by_start_time_within_same_owner()
    {
        var sourceA = Guid.NewGuid();
        var t0 = new DateTime(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

        // Same source contributes overlapping batches — second batch's duplicate StartTime should be dropped.
        var firstBatch = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ch1"] = new() { MakeProgram("p1", "ch1", t0), MakeProgram("p2", "ch1", t0.AddMinutes(30)) }
        };
        var secondBatch = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ch1"] = new() { MakeProgram("p1-dup", "ch1", t0), MakeProgram("p3", "ch1", t0.AddMinutes(60)) }
        };

        var merged = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
        var claimedBy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        IptvEpgService.ClaimChannelsForSource(sourceA, firstBatch, merged, claimedBy);
        IptvEpgService.ClaimChannelsForSource(sourceA, secondBatch, merged, claimedBy);

        merged["ch1"].Should().HaveCount(3);
        merged["ch1"].Select(p => p.Id).Should().Contain(new[] { "p1", "p2", "p3" });
        merged["ch1"].Should().NotContain(p => p.Id == "p1-dup");
    }

    [Fact]
    public void ClaimChannelsForSource_allows_different_sources_to_own_different_channels()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var t0 = new DateTime(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

        var parsedA = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ch1"] = new() { MakeProgram("a1", "ch1", t0) }
        };
        var parsedB = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ch2"] = new() { MakeProgram("b1", "ch2", t0) }
        };

        var merged = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
        var claimedBy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        IptvEpgService.ClaimChannelsForSource(sourceA, parsedA, merged, claimedBy);
        IptvEpgService.ClaimChannelsForSource(sourceB, parsedB, merged, claimedBy);

        claimedBy["ch1"].Should().Be(sourceA);
        claimedBy["ch2"].Should().Be(sourceB);
        merged.Should().HaveCount(2);
    }

    [Fact]
    public void ClaimChannelsForSource_channel_id_match_is_case_insensitive()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var t0 = new DateTime(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

        var parsedA = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CHANNEL_one"] = new() { MakeProgram("a1", "CHANNEL_one", t0) }
        };
        var parsedB = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["channel_ONE"] = new() { MakeProgram("b1", "channel_ONE", t0.AddHours(1)) }
        };

        var merged = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
        var claimedBy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        IptvEpgService.ClaimChannelsForSource(sourceA, parsedA, merged, claimedBy);
        IptvEpgService.ClaimChannelsForSource(sourceB, parsedB, merged, claimedBy);

        merged.Should().HaveCount(1);
        merged.Values.First().Select(p => p.Id).Should().BeEquivalentTo(new[] { "a1" });
    }

    [Fact]
    public void ApplyParentalControls_redacts_program_with_rating_outside_allowlist()
    {
        var guide = new Dictionary<string, List<IptvProgramDto>>
        {
            ["ch1"] = new() { MakeProgram("p1", "ch1", DateTime.UtcNow, rating: "TV-MA", title: "Mature Show", description: "adult content") }
        };

        IptvEpgService.ApplyParentalControls(guide, new List<string> { "TV-G", "TV-PG" }, blockUnratedContent: false);

        guide["ch1"][0].Title.Should().Be("Restricted Content");
        guide["ch1"][0].Description.Should().Be("This program exceeds the content rating limits for this profile.");
    }

    [Fact]
    public void ApplyParentalControls_leaves_program_in_allowlist_unchanged()
    {
        var guide = new Dictionary<string, List<IptvProgramDto>>
        {
            ["ch1"] = new() { MakeProgram("p1", "ch1", DateTime.UtcNow, rating: "TV-PG", title: "Family Show", description: "fun") }
        };

        IptvEpgService.ApplyParentalControls(guide, new List<string> { "TV-G", "TV-PG" }, blockUnratedContent: false);

        guide["ch1"][0].Title.Should().Be("Family Show");
        guide["ch1"][0].Description.Should().Be("fun");
    }

    [Fact]
    public void ApplyParentalControls_passes_unrated_when_blockUnratedContent_is_false()
    {
        var guide = new Dictionary<string, List<IptvProgramDto>>
        {
            ["ch1"] = new() { MakeProgram("p1", "ch1", DateTime.UtcNow, rating: "NR", title: "Unrated", description: "no rating") }
        };

        IptvEpgService.ApplyParentalControls(guide, new List<string> { "TV-G" }, blockUnratedContent: false);

        guide["ch1"][0].Title.Should().Be("Unrated");
    }

    [Fact]
    public void ApplyParentalControls_redacts_unrated_when_blockUnratedContent_is_true()
    {
        var guide = new Dictionary<string, List<IptvProgramDto>>
        {
            ["ch1"] = new() { MakeProgram("p1", "ch1", DateTime.UtcNow, rating: "NR", title: "Unrated", description: "no rating") }
        };

        IptvEpgService.ApplyParentalControls(guide, new List<string> { "TV-G" }, blockUnratedContent: true);

        guide["ch1"][0].Title.Should().Be("Restricted Content");
    }

    [Fact]
    public void ApplyParentalControls_redacts_only_offending_program_in_mixed_channel()
    {
        var guide = new Dictionary<string, List<IptvProgramDto>>
        {
            ["ch1"] = new()
            {
                MakeProgram("ok", "ch1", DateTime.UtcNow, rating: "TV-G", title: "Cartoons"),
                MakeProgram("bad", "ch1", DateTime.UtcNow.AddMinutes(30), rating: "TV-MA", title: "Adult")
            }
        };

        IptvEpgService.ApplyParentalControls(guide, new List<string> { "TV-G" }, blockUnratedContent: true);

        guide["ch1"][0].Title.Should().Be("Cartoons");
        guide["ch1"][1].Title.Should().Be("Restricted Content");
    }
}
