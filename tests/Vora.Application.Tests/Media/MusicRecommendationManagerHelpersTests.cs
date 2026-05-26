using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Media;

public class MusicRecommendationManagerHelpersTests
{
    // Covers the pure algorithmic helpers used by mix-building.
    // The full mix-building flow consults an IRecommendationRepository whose
    // shape doesn't easily lend itself to NSubstitute (lots of fan-out reads),
    // so we test the algorithms directly.

    private static ArtistPlayScore Score(Guid id, string name, double score) => new()
    {
        ArtistId = id,
        ArtistName = name,
        Score = score
    };

    private static Track Trk(Guid id, Guid? artistId = null) => new()
    {
        Id = id,
        Title = id.ToString("N").Substring(0, 6),
        Album = artistId is null ? null : new Album { Title = "A", ArtistId = artistId.Value }
    };

    // ---------- ProfileHasFewPlays ----------

    [Fact]
    public void ProfileHasFewPlays_true_when_sum_of_scores_below_threshold()
    {
        var scores = new List<ArtistPlayScore>
        {
            Score(Guid.NewGuid(), "a", 3),
            Score(Guid.NewGuid(), "b", 4)
        };

        MusicRecommendationManager.ProfileHasFewPlays(scores, minPlays: 10).Should().BeTrue();
    }

    [Fact]
    public void ProfileHasFewPlays_false_when_sum_meets_or_exceeds_threshold()
    {
        var scores = new List<ArtistPlayScore>
        {
            Score(Guid.NewGuid(), "a", 6),
            Score(Guid.NewGuid(), "b", 4)
        };

        MusicRecommendationManager.ProfileHasFewPlays(scores, minPlays: 10).Should().BeFalse();
    }

    [Fact]
    public void ProfileHasFewPlays_true_for_empty_history()
    {
        MusicRecommendationManager.ProfileHasFewPlays(new List<ArtistPlayScore>(), minPlays: 1).Should().BeTrue();
    }

    // ---------- DedupeById ----------

    [Fact]
    public void DedupeById_preserves_first_occurrence_order()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var input = new List<Track> { Trk(id1), Trk(id2), Trk(id1), Trk(id3), Trk(id2) };

        var deduped = MusicRecommendationManager.DedupeById(input);

        deduped.Select(t => t.Id).Should().Equal(id1, id2, id3);
    }

    [Fact]
    public void DedupeById_returns_empty_list_for_empty_input()
    {
        MusicRecommendationManager.DedupeById(new List<Track>()).Should().BeEmpty();
    }

    // ---------- InterleaveForVariety ----------

    [Fact]
    public void InterleaveForVariety_avoids_consecutive_same_artist_when_possible()
    {
        var artistA = Guid.NewGuid();
        var artistB = Guid.NewGuid();
        var input = new List<Track>
        {
            Trk(Guid.NewGuid(), artistA),
            Trk(Guid.NewGuid(), artistA),
            Trk(Guid.NewGuid(), artistA),
            Trk(Guid.NewGuid(), artistB),
            Trk(Guid.NewGuid(), artistB)
        };

        var output = MusicRecommendationManager.InterleaveForVariety(input, maxConsecutiveSameArtist: 1, maxPerArtist: 3);

        // First three picks should alternate where possible; never have 3+ artistA in a row at the head.
        for (var i = 0; i < output.Count - 2; i++)
        {
            var window = output.Skip(i).Take(3).Select(t => t.Album!.ArtistId).Distinct().Count();
            // when both artists have tracks remaining, we expect at least 2 distinct in any 3-window
            // (the algorithm breaks the tie by queue depth, not strict alternation, so we just check that
            // we don't get a block of three identical artist ids while the other queue is non-empty)
            if (output.Skip(i).Take(3).All(t => t.Album!.ArtistId == artistA))
            {
                // 3-in-a-row of artistA is only acceptable if artistB queue ran dry by that point.
                output.Take(i).Count(t => t.Album!.ArtistId == artistB).Should().Be(2);
            }
        }
    }

    [Fact]
    public void InterleaveForVariety_caps_at_maxPerArtist()
    {
        var artistA = Guid.NewGuid();
        var input = Enumerable.Range(0, 10).Select(_ => Trk(Guid.NewGuid(), artistA)).ToList();

        var output = MusicRecommendationManager.InterleaveForVariety(input, maxConsecutiveSameArtist: 1, maxPerArtist: 3);

        output.Should().HaveCount(3);
        output.Select(t => t.Album!.ArtistId).Should().AllBeEquivalentTo(artistA);
    }

    [Fact]
    public void InterleaveForVariety_handles_tracks_with_no_album()
    {
        var input = new List<Track> { Trk(Guid.NewGuid()), Trk(Guid.NewGuid()) };

        var output = MusicRecommendationManager.InterleaveForVariety(input, maxConsecutiveSameArtist: 1, maxPerArtist: 5);

        output.Should().HaveCount(2);
    }

    // ---------- DriftBlend ----------

    [Fact]
    public void DriftBlend_returns_fresh_when_existing_empty()
    {
        var fresh = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        MusicRecommendationManager.DriftBlend(new List<Guid>(), fresh, 30).Should().BeEquivalentTo(fresh);
    }

    [Fact]
    public void DriftBlend_returns_existing_when_fresh_empty()
    {
        var existing = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        MusicRecommendationManager.DriftBlend(existing, new List<Guid>(), 30).Should().BeEquivalentTo(existing);
    }

    [Fact]
    public void DriftBlend_evicts_tail_and_appends_newcomers_preserving_size()
    {
        var keep1 = Guid.NewGuid();
        var keep2 = Guid.NewGuid();
        var keep3 = Guid.NewGuid();
        var evict1 = Guid.NewGuid();
        var evict2 = Guid.NewGuid();
        var fresh1 = Guid.NewGuid();
        var fresh2 = Guid.NewGuid();

        var existing = new List<Guid> { keep1, keep2, keep3, evict1, evict2 };
        var fresh = new List<Guid> { fresh1, fresh2, keep1, Guid.NewGuid() };

        // 40% drift on a 5-element list → ceil(5 * 0.4) = 2 evictions
        var result = MusicRecommendationManager.DriftBlend(existing, fresh, 40);

        result.Should().HaveCount(5);
        result.Take(3).Should().Equal(keep1, keep2, keep3);
        result.Should().NotContain(evict1);
        result.Should().NotContain(evict2);
        result.Skip(3).Should().HaveCount(2);
        // Newcomers should skip any id already in the keep set
        result.Skip(3).Should().NotContain(keep1);
    }

    [Fact]
    public void DriftBlend_always_evicts_at_least_one_at_low_drift_percent()
    {
        var existing = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var fresh = new List<Guid> { Guid.NewGuid() };

        var result = MusicRecommendationManager.DriftBlend(existing, fresh, 1);

        // ceil(10 * 0.01) = 1 → exactly one evicted
        result.Should().HaveCount(10);
        result[9].Should().Be(fresh[0]);
    }

    [Fact]
    public void DriftBlend_clamps_evictions_to_existing_count_minus_one()
    {
        // Drift percent ridiculously high — algo should still keep at least one of existing.
        var existing = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var fresh = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        var result = MusicRecommendationManager.DriftBlend(existing, fresh, 200);

        result.Should().HaveCount(4);
        result[0].Should().Be(existing[0]);
    }
}
