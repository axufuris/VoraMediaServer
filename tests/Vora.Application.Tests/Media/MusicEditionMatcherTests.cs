using Vora.Application.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// An untagged file cannot say which edition of its album it is, and Deezer
// carries both "The Eminem Show"s with durations within a second of each other.
// These pin the one rule that makes that safe: a track is Clean only when every
// edition it matches says so.
public class MusicEditionMatcherTests
{
    private static ProviderAlbumEdition Edition(string title, params (string Title, int Duration, ProviderAdvisory Advisory)[] tracks) => new()
    {
        Title = title,
        ArtistName = "Eminem",
        Tracks = tracks.Select(t => new ProviderEditionTrack { Title = t.Title, DurationSeconds = t.Duration, Advisory = t.Advisory }).ToList()
    };

    private static readonly ProviderAlbumEdition ExplicitEdition = Edition("The Eminem Show",
        ("White America", 324, ProviderAdvisory.Explicit), ("Cleanin' Out My Closet", 297, ProviderAdvisory.Explicit));

    private static readonly ProviderAlbumEdition CleanEdition = Edition("The Eminem Show",
        ("White America", 324, ProviderAdvisory.Clean), ("Cleanin' Out My Closet", 298, ProviderAdvisory.Clean));

    [Fact]
    public void When_both_editions_match_the_explicit_one_wins()
    {
        MusicEditionMatcher.Resolve("White America", 324, new[] { CleanEdition, ExplicitEdition })
            .Should().Be(ProviderAdvisory.Explicit);
    }

    [Fact]
    public void Clean_only_when_every_matching_edition_is_clean()
    {
        MusicEditionMatcher.Resolve("Cleanin' Out My Closet", 298, new[] { CleanEdition })
            .Should().Be(ProviderAdvisory.Clean);
    }

    // No opinion from one edition is not agreement.
    [Fact]
    public void An_edition_with_no_opinion_keeps_the_track_unrated()
    {
        var unknown = Edition("The Eminem Show", ("White America", 324, ProviderAdvisory.Unknown));

        MusicEditionMatcher.Resolve("White America", 324, new[] { CleanEdition, unknown })
            .Should().Be(ProviderAdvisory.Unknown);
    }

    [Fact]
    public void A_different_cut_of_the_song_is_not_a_match()
    {
        var live = Edition("The Eminem Show", ("White America", 402, ProviderAdvisory.Clean));

        MusicEditionMatcher.Resolve("White America", 324, new[] { live })
            .Should().Be(ProviderAdvisory.Unknown);
    }

    [Fact]
    public void An_edition_label_on_the_track_title_does_not_hide_the_match()
    {
        var labelled = Edition("The Eminem Show", ("White America (Explicit Version)", 324, ProviderAdvisory.Explicit));

        MusicEditionMatcher.Resolve("White America", 324, new[] { labelled })
            .Should().Be(ProviderAdvisory.Explicit);
    }

    // Other brackets name a different recording and stay part of the title.
    [Fact]
    public void A_piano_version_is_not_the_song()
    {
        var piano = Edition("The Eminem Show", ("White America (Piano Version)", 324, ProviderAdvisory.Clean));

        MusicEditionMatcher.Resolve("White America", 324, new[] { piano })
            .Should().Be(ProviderAdvisory.Unknown);
    }

    [Fact]
    public void Editions_of_the_album_include_deluxe_and_labelled_ones_by_the_same_artist()
    {
        var expanded = Edition("The Eminem Show (Expanded Edition)");
        var tribute = new ProviderAlbumEdition { Title = "The Eminem Show", ArtistName = "Piano Tribute Players" };
        var other = Edition("Encore");

        MusicEditionMatcher.EditionsOf("Eminem", "The Eminem Show", new[] { ExplicitEdition, expanded, tribute, other })
            .Should().BeEquivalentTo(new[] { ExplicitEdition, expanded });
    }
}
