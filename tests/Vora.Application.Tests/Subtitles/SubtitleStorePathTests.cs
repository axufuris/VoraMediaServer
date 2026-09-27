using Vora.Application.Settings;
using Vora.Application.Subtitles;

namespace Vora.Application.Tests.Subtitles;

// StoragePaths:Subtitles arrived after most servers were already deployed, so an
// unset value is the NORMAL case on an existing install, not an edge one.
// Resolving it to AppContext.BaseDirectory put it at /app/subtitles inside the
// container — part of the read-only image — and every download failed after the
// provider had already charged a unit of the daily quota.
public class SubtitleStorePathTests
{
    // The container runs Linux; the tests run on Windows, where GetDirectoryName
    // normalises separators. Expectations are built with the same primitives as
    // the code so the assertion is about the RESOLUTION, not the separator.
    private static string Expected(string dataDirectory) =>
        Path.Combine(Path.GetDirectoryName(dataDirectory.TrimEnd('/', '\\'))!, "subtitles");

    private static StoragePathsOptions Paths(string? subtitles = null, string? thumbnails = null, string? artwork = null) => new()
    {
        Subtitles = subtitles,
        VideoThumbnails = thumbnails,
        CustomArtwork = artwork,
    };

    [Fact]
    public void An_explicit_setting_is_used_as_given()
    {
        SubtitleStorePath.Resolve(Paths(subtitles: "/app/data/subtitles")).Should().Be("/app/data/subtitles");
    }

    // The fallback lands beside a path the deployment already mounts and already
    // persists, so an existing server gains the feature with no compose change.
    [Fact]
    public void An_unset_path_lands_beside_the_other_data_directories()
    {
        var resolved = SubtitleStorePath.Resolve(Paths(thumbnails: "/app/data/video-thumbnails"));

        resolved.Should().Be(Expected("/app/data/video-thumbnails"));
    }

    [Fact]
    public void Any_configured_data_directory_will_do()
    {
        var resolved = SubtitleStorePath.Resolve(Paths(artwork: "/app/data/custom_artwork"));

        resolved.Should().Be(Expected("/app/data/custom_artwork"));
    }

    // A trailing separator is easy to leave in a compose file and must not make
    // the parent resolve to the directory itself.
    [Fact]
    public void A_trailing_separator_does_not_confuse_the_parent()
    {
        SubtitleStorePath.Resolve(Paths(thumbnails: "/app/data/video-thumbnails/"))
            .Should().Be(Expected("/app/data/video-thumbnails"));
    }

    [Fact]
    public void The_explicit_setting_wins_over_the_fallback()
    {
        var resolved = SubtitleStorePath.Resolve(Paths(subtitles: "/mnt/subs", thumbnails: "/app/data/video-thumbnails"));

        resolved.Should().Be("/mnt/subs");
    }

    // Nothing configured at all is a bare-metal run, where the app directory is
    // writable and is the right answer.
    [Fact]
    public void With_nothing_configured_it_falls_back_to_the_app_directory()
    {
        SubtitleStorePath.Resolve(Paths()).Should().Be(Path.Combine(AppContext.BaseDirectory, "subtitles"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_setting_is_treated_as_unset(string configured)
    {
        SubtitleStorePath.Resolve(Paths(subtitles: configured, thumbnails: "/app/data/video-thumbnails"))
            .Should().Be(Expected("/app/data/video-thumbnails"));
    }

    // FFmpeg picks its demuxer partly from the file extension, so the staged
    // download keeps the format the provider reported rather than a neutral
    // suffix it would have to guess at.
    [Theory]
    [InlineData("srt", "srt")]
    [InlineData(".ASS", "ass")]
    [InlineData("vtt", "vtt")]
    [InlineData("sub", "sub")]
    [InlineData("zip", "srt")]
    [InlineData(null, "srt")]
    public void The_staged_download_keeps_a_format_ffmpeg_recognises(string? format, string expected)
    {
        SubtitleSearchManager.NormalizeExtension(format).Should().Be(expected);
    }
}
