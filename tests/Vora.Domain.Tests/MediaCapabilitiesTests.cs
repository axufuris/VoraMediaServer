using Vora.Domain.Entities.Media;

namespace Vora.Domain.Tests;

// These tests ARE the definition of what each capability means. A guard spelled
// `m is Movie || m is TvShow` states its membership and never its intent, so
// changing the membership is indistinguishable from fixing a typo. Here the
// membership is asserted per type and per capability, so widening one is a
// deliberate edit to a named rule with a reason attached.
public class MediaCapabilitiesTests
{
    private static readonly MediaItem Movie = new Movie { Title = "Inception" };
    private static readonly MediaItem Show = new TvShow { Title = "Severance" };
    private static readonly MediaItem Season = new Season { Title = "Season 1" };
    private static readonly MediaItem Episode = new Episode { Title = "Good News About Hell" };
    private static readonly MediaItem Track = new Track { Title = "Mr Self Destruct" };

    public static TheoryData<MediaItem, bool> ProviderIdentity => new()
    {
        { Movie, true },
        { Show, true },
        { Season, false },
        { Episode, false },
        { Track, false },
    };

    [Theory]
    [MemberData(nameof(ProviderIdentity))]
    public void HasProviderIdentity_covers_the_titles_a_provider_is_searched_for(MediaItem item, bool expected) =>
        item.CanBeMatchedToAProvider().Should().Be(expected);

    public static TheoryData<MediaItem, bool> Enrichment => new()
    {
        { Movie, true },
        { Show, true },
        // A season's poster and an episode's title come from the show's mapping,
        // so both are enriched even though neither is looked up on its own.
        { Season, true },
        { Episode, true },
        // A track's metadata comes from its tags and its artwork from
        // MusicManager, so the generic pipeline has nothing to do with it.
        { Track, false },
    };

    [Theory]
    [MemberData(nameof(Enrichment))]
    public void SupportsMetadataEnrichment_is_broader_than_provider_identity(MediaItem item, bool expected) =>
        item.CanBeEnriched().Should().Be(expected);

    public static TheoryData<MediaItem, bool> Browsable => new()
    {
        { Movie, true },
        { Show, true },
        { Season, false },
        { Episode, false },
        { Track, false },
    };

    [Theory]
    [MemberData(nameof(Browsable))]
    public void IsBrowsableTitle_covers_top_level_browse_entries(MediaItem item, bool expected) =>
        item.CanBeBrowsedAsATitle().Should().Be(expected);

    public static TheoryData<MediaItem, bool> Playable => new()
    {
        { Movie, true },
        { Episode, true },
        { Track, true },
        { Show, false },
        { Season, false },
    };

    [Theory]
    [MemberData(nameof(Playable))]
    public void HasPlayableParts_covers_the_leaves_that_own_files(MediaItem item, bool expected) =>
        item.CanHavePlayableParts().Should().Be(expected);

    public static TheoryData<MediaItem, bool> SoftDeletable => new()
    {
        { Movie, true },
        { Show, true },
        { Season, true },
        { Episode, true },
        // Media Trash is video-only; a Track goes immediately.
        { Track, false },
    };

    [Theory]
    [MemberData(nameof(SoftDeletable))]
    public void IsSoftDeletable_is_everything_except_music(MediaItem item, bool expected) =>
        item.CanBeSoftDeleted().Should().Be(expected);

    public static TheoryData<MediaItem, bool> PlayableVideo => new()
    {
        { Movie, true },
        { Episode, true },
        // A track is playable but has no frames, so thumbnails and the
        // black-frame and silence passes have nothing to work on.
        { Track, false },
        { Show, false },
        { Season, false },
    };

    [Theory]
    [MemberData(nameof(PlayableVideo))]
    public void IsPlayableVideo_excludes_music(MediaItem item, bool expected) =>
        item.IsPlayableVideoItem().Should().Be(expected);

    public static TheoryData<MediaItem, bool> PartOfATvShow => new()
    {
        { Show, true },
        { Season, true },
        { Episode, true },
        { Movie, false },
        { Track, false },
    };

    [Theory]
    [MemberData(nameof(PartOfATvShow))]
    public void IsPartOfATvShow_covers_the_whole_show_tree(MediaItem item, bool expected) =>
        item.BelongsToATvShow().Should().Be(expected);

    // History filtering offers Movies and TV Shows as the two arms of one choice,
    // so anything that is neither would fall out of both. Asserting the partition
    // is what makes adding a type to one arm visibly a decision about the other.
    [Fact]
    public void A_movie_is_the_only_thing_outside_the_show_tree_that_history_offers()
    {
        Movie.BelongsToATvShow().Should().BeFalse();
        Track.BelongsToATvShow().Should().BeFalse("music is neither arm of the history type filter");
    }

    // IsPlayableVideo is strictly narrower than HasPlayableParts. If that ever
    // stops holding, one of the two has been widened without the other.
    [Fact]
    public void Every_playable_video_is_also_a_playable_item()
    {
        foreach (var item in new[] { Movie, Show, Season, Episode, Track })
        {
            if (item.IsPlayableVideoItem()) item.CanHavePlayableParts().Should().BeTrue();
        }
    }

    // Same membership, different questions. Asserting they agree TODAY documents
    // that the overlap is a coincidence of the current model rather than a rule —
    // an album becoming browsable would move one and not the other.
    [Fact]
    public void Provider_identity_and_browsability_agree_today_without_being_the_same_rule()
    {
        foreach (var item in new[] { Movie, Show, Season, Episode, Track })
        {
            item.CanBeMatchedToAProvider().Should().Be(item.CanBeBrowsedAsATitle());
        }

        ReferenceEquals(MediaCapabilities.HasProviderIdentity, MediaCapabilities.IsBrowsableTitle)
            .Should().BeFalse("they are separate rules that happen to agree, not one rule under two names");
    }

    // The expression is what EF translates; the Func is what in-memory callers
    // use. They have to stay the same rule, or a query and a check on the same
    // item could disagree.
    [Fact]
    public void The_compiled_form_matches_the_expression_it_came_from()
    {
        var items = new[] { Movie, Show, Season, Episode, Track };

        foreach (var item in items)
        {
            MediaCapabilities.HasProviderIdentity.Compile()(item).Should().Be(item.CanBeMatchedToAProvider());
            MediaCapabilities.SupportsMetadataEnrichment.Compile()(item).Should().Be(item.CanBeEnriched());
            MediaCapabilities.IsBrowsableTitle.Compile()(item).Should().Be(item.CanBeBrowsedAsATitle());
            MediaCapabilities.HasPlayableParts.Compile()(item).Should().Be(item.CanHavePlayableParts());
            MediaCapabilities.IsSoftDeletable.Compile()(item).Should().Be(item.CanBeSoftDeleted());
            MediaCapabilities.IsPlayableVideo.Compile()(item).Should().Be(item.IsPlayableVideoItem());
            MediaCapabilities.IsPartOfATvShow.Compile()(item).Should().Be(item.BelongsToATvShow());
        }
    }

    private sealed class Holder
    {
        public MediaItem Item { get; init; } = null!;
    }

    // On() rebinds the capability's parameter onto a path rather than invoking it,
    // because an Invoke node is what EF refuses to translate. The rebound
    // expression has to select exactly what the original selects.
    [Fact]
    public void On_asks_the_same_question_through_a_navigation_path()
    {
        var reboundBrowsable = MediaCapabilities.IsBrowsableTitle.On((Holder h) => h.Item).Compile();

        foreach (var item in new[] { Movie, Show, Season, Episode, Track })
        {
            reboundBrowsable(new Holder { Item = item }).Should().Be(item.CanBeBrowsedAsATitle());
        }
    }

    [Fact]
    public void On_produces_a_lambda_over_the_owner_rather_than_the_media_item()
    {
        var rebound = MediaCapabilities.IsPartOfATvShow.On((Holder h) => h.Item);

        rebound.Parameters.Should().HaveCount(1);
        rebound.Parameters[0].Type.Should().Be<Holder>();
        rebound.ToString().Should().NotContain("Invoke", "an Invoke node is exactly what EF Core cannot translate");
    }
}
