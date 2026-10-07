using Vora.Application.Media;

namespace Vora.Application.Tests.Media;

public class SameSongTests
{
    private sealed record Copy(string Name, SongFacts Facts);

    private static List<string> Kept(params Copy[] copies) =>
        SameSong.Group(copies, c => c.Facts).Select(s => s.Best.Name).ToList();

    [Fact]
    public void The_album_and_single_copies_of_a_song_are_one_song_and_the_lossless_copy_wins()
    {
        var kept = Kept(
            new Copy("single mp3", new SongFacts("The Chainsmokers", "High", 175, "mp3", 44100, 320)),
            new Copy("album flac", new SongFacts("The Chainsmokers", "High", 175, "flac", 44100, 959)));

        kept.Should().Equal("album flac");
    }

    [Fact]
    public void Between_lossy_copies_the_higher_bitrate_wins()
    {
        var kept = Kept(
            new Copy("128", new SongFacts("Daft Punk", "Musique", 270, "mp3", 44100, 128)),
            new Copy("320", new SongFacts("Daft Punk", "Musique", 271, "mp3", 44100, 320)));

        kept.Should().Equal("320");
    }

    [Fact]
    public void Between_lossless_copies_the_higher_sample_rate_wins()
    {
        var kept = Kept(
            new Copy("cd", new SongFacts("Daft Punk", "Musique", 270, "flac", 44100, 900)),
            new Copy("hi-res", new SongFacts("Daft Punk", "Musique", 270, "flac", 96000, 2800)));

        kept.Should().Equal("hi-res");
    }

    [Fact]
    public void The_kept_copy_takes_the_place_of_the_first_one()
    {
        var kept = Kept(
            new Copy("high mp3", new SongFacts("The Chainsmokers", "High", 175, "mp3", 44100, 320)),
            new Copy("rap god", new SongFacts("Eminem", "Rap God", 363, "mp3", 44100, 320)),
            new Copy("high flac", new SongFacts("The Chainsmokers", "High", 175, "flac", 44100, 959)));

        kept.Should().Equal("high flac", "rap god");
    }

    [Theory]
    [InlineData("High", "high")]
    [InlineData("Don’t Stop", "Don't Stop")]
    [InlineData("Without Me (Explicit)", "Without Me")]
    [InlineData("Come Together (Remastered 2009)", "Come Together")]
    [InlineData("Come Together - 2019 Remaster", "Come Together")]
    [InlineData("Crazy in Love (feat. Jay-Z)", "Crazy in Love")]
    [InlineData("Umbrella (Album Version)", "Umbrella")]
    public void Spelling_and_edition_labels_do_not_make_a_different_song(string a, string b)
    {
        SameSong.Same(new SongFacts("Artist", a, 200), new SongFacts("Artist", b, 201)).Should().BeTrue();
    }

    [Theory]
    [InlineData("Kansas (Piano Version)", "Kansas")]
    [InlineData("High (Live)", "High")]
    [InlineData("Musique (Remix)", "Musique")]
    public void A_different_recording_stays_a_different_song(string a, string b)
    {
        SameSong.Same(new SongFacts("Artist", a, 200), new SongFacts("Artist", b, 200)).Should().BeFalse();
    }

    [Fact]
    public void A_featured_artist_in_the_artist_tag_is_the_same_artist()
    {
        SameSong.Same(new SongFacts("Eminem feat. Rihanna", "The Monster", 250), new SongFacts("Eminem", "The Monster", 250)).Should().BeTrue();
    }

    [Fact]
    public void Two_songs_with_the_same_name_and_different_lengths_stay_apart()
    {
        var kept = Kept(
            new Copy("intro one", new SongFacts("Eminem", "Intro", 42)),
            new Copy("intro two", new SongFacts("Eminem", "Intro", 95)));

        kept.Should().Equal("intro one", "intro two");
    }

    [Fact]
    public void The_same_title_by_different_artists_is_a_different_song()
    {
        SameSong.Same(new SongFacts("The Chainsmokers", "High", 175), new SongFacts("Lighthouse Family", "High", 175)).Should().BeFalse();
    }

    [Fact]
    public void A_song_without_a_title_or_artist_is_never_merged()
    {
        var kept = Kept(
            new Copy("a", new SongFacts(null, "High", 175)),
            new Copy("b", new SongFacts(null, "High", 175)),
            new Copy("c", new SongFacts("Artist", null, 175)),
            new Copy("d", new SongFacts("Artist", null, 175)));

        kept.Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public void A_missing_length_does_not_keep_two_copies_apart()
    {
        SameSong.Same(new SongFacts("Daft Punk", "Musique", null), new SongFacts("Daft Punk", "Musique", 270)).Should().BeTrue();
    }
}
