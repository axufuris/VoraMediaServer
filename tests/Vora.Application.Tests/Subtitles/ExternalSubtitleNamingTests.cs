using Vora.Application.Subtitles;

namespace Vora.Application.Tests.Subtitles;

// Everything a sidecar track knows about itself — language, forced, hearing-
// impaired — is encoded in dot-separated segments of its filename. Parsing them
// is what turns an anonymous file into a selectable track.
public class ExternalSubtitleNamingTests
{
    private const string Video = "Movie (2026).mkv";

    private static ExternalSubtitleFile? Parse(string subtitleFileName) =>
        ExternalSubtitleNaming.TryParse(Video, $"/media/{subtitleFileName}");

    [Fact]
    public void A_bare_sidecar_matches_with_no_language()
    {
        var parsed = Parse("Movie (2026).srt");

        parsed.Should().NotBeNull();
        parsed!.Codec.Should().Be("subrip");
        parsed.Language.Should().BeNull();
        parsed.IsForced.Should().BeFalse();
        parsed.IsSdh.Should().BeFalse();
    }

    [Theory]
    [InlineData("Movie (2026).en.srt", "en")]
    [InlineData("Movie (2026).eng.srt", "eng")]
    [InlineData("Movie (2026).pt-BR.srt", "pt-br")]
    public void The_language_segment_is_read(string fileName, string expected)
    {
        Parse(fileName)!.Language.Should().Be(expected);
    }

    [Fact]
    public void Forced_and_language_are_read_together()
    {
        var parsed = Parse("Movie (2026).en.forced.srt");

        parsed!.Language.Should().Be("en");
        parsed.IsForced.Should().BeTrue();
    }

    // There is no hearing-impaired flag on the track, so it goes in the title,
    // which is what the picker shows.
    [Theory]
    [InlineData("Movie (2026).en.sdh.srt")]
    [InlineData("Movie (2026).en.cc.srt")]
    public void Hearing_impaired_markers_land_in_the_title(string fileName)
    {
        var parsed = Parse(fileName);

        parsed!.IsSdh.Should().BeTrue();
        parsed.Title.Should().Be("EN SDH");
    }

    // The picker prefers Title over Language, so a bare "SDH" would make two
    // hearing-impaired sidecars in different languages read the same.
    [Fact]
    public void Two_hearing_impaired_sidecars_in_different_languages_are_distinguishable()
    {
        Parse("Movie (2026).en.sdh.srt")!.Title.Should().NotBe(Parse("Movie (2026).fr.sdh.srt")!.Title);
    }

    [Fact]
    public void A_hearing_impaired_sidecar_with_no_language_still_says_so()
    {
        Parse("Movie (2026).sdh.srt")!.Title.Should().Be("SDH");
    }

    [Fact]
    public void Segment_order_does_not_matter()
    {
        var parsed = Parse("Movie (2026).forced.en.srt");

        parsed!.Language.Should().Be("en");
        parsed.IsForced.Should().BeTrue();
    }

    [Theory]
    [InlineData("Movie (2026).srt", "subrip")]
    [InlineData("Movie (2026).ass", "ass")]
    [InlineData("Movie (2026).ssa", "ssa")]
    [InlineData("Movie (2026).vtt", "webvtt")]
    [InlineData("Movie (2026).sub", "vobsub")]
    public void The_codec_comes_from_the_extension(string fileName, string expected)
    {
        Parse(fileName)!.Codec.Should().Be(expected);
    }

    // The separator must be a literal dot. Without that rule a sidecar for a
    // different cut in the same folder attaches itself to the wrong video, and
    // the viewer gets subtitles that drift against the picture.
    [Theory]
    [InlineData("Movie (2026) Part 2.en.srt")]
    [InlineData("Movie (2026) Extended.srt")]
    [InlineData("Another Movie.en.srt")]
    public void A_sidecar_for_a_different_video_does_not_match(string fileName)
    {
        Parse(fileName).Should().BeNull();
    }

    [Theory]
    [InlineData("Movie (2026).txt")]
    [InlineData("Movie (2026).nfo")]
    [InlineData("Movie (2026).mkv")]
    public void A_file_that_is_not_a_subtitle_does_not_match(string fileName)
    {
        Parse(fileName).Should().BeNull();
    }

    // Libraries come off every OS and every naming tool; casing is not a signal.
    [Fact]
    public void Matching_ignores_casing()
    {
        var parsed = ExternalSubtitleNaming.TryParse("Movie (2026).MKV", "/media/movie (2026).EN.FORCED.SRT");

        parsed.Should().NotBeNull();
        parsed!.Language.Should().Be("en");
        parsed.IsForced.Should().BeTrue();
    }

    // A release-group tag or similar noise shouldn't be mistaken for a language,
    // and shouldn't stop the rest of the name parsing either.
    [Fact]
    public void An_unrecognised_segment_is_ignored()
    {
        var parsed = Parse("Movie (2026).SOMEGROUP.en.srt");

        parsed!.Language.Should().Be("en");
    }

    [Fact]
    public void The_full_path_is_carried_through()
    {
        Parse("Movie (2026).en.srt")!.FilePath.Should().Be("/media/Movie (2026).en.srt");
    }
}
