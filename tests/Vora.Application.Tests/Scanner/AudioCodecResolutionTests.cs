using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class AudioCodecResolutionTests
{
    // Track.AudioCodec is varchar(32). TagLib's Properties.Description for a VBR
    // mp3 is 33 characters, which threw 22001 mid-scan and took the whole library
    // task down with it.
    private const int AudioCodecColumnLength = 32;

    [Theory]
    [InlineData("/media/music/a/b/01.mp3", "MPEG Version 1 Audio, Layer 3 VBR", "mp3")]
    [InlineData("/media/music/a/b/01.mp3", "MPEG Version 1 Audio, Layer 3", "mp3")]
    [InlineData("/media/music/a/b/01.flac", "Flac Audio", "flac")]
    [InlineData("/media/music/a/b/01.opus", null, "opus")]
    [InlineData("/media/music/a/b/01.wav", null, "pcm")]
    [InlineData("/media/music/a/b/01.ogg", null, "vorbis")]
    [InlineData("/media/music/a/b/01.WMA", null, "wma")]
    public void Resolves_a_short_codec_token_from_the_container(string path, string? description, string expected)
    {
        VoraLocalMediaScannerProvider.ResolveAudioCodec(path, description).Should().Be(expected);
    }

    [Theory]
    [InlineData("Apple Lossless Audio Codec", "alac")]
    [InlineData("MPEG-4 Audio (mp4a)", "aac")]
    public void Disambiguates_the_two_codecs_that_share_the_m4a_container(string description, string expected)
    {
        VoraLocalMediaScannerProvider.ResolveAudioCodec("/media/music/a/b/01.m4a", description).Should().Be(expected);
    }

    [Fact]
    public void Never_returns_a_value_too_long_for_the_column()
    {
        var descriptions = new[]
        {
            "MPEG Version 1 Audio, Layer 3 VBR",
            new string('x', 500),
            null,
        };

        var paths = new[]
        {
            "/m/01.mp3", "/m/01.flac", "/m/01.m4a", "/m/01.aac", "/m/01.ogg",
            "/m/01.opus", "/m/01.wav", "/m/01.wma", "/m/01.weird",
        };

        foreach (var path in paths)
        {
            foreach (var description in descriptions)
            {
                var codec = VoraLocalMediaScannerProvider.ResolveAudioCodec(path, description);
                (codec?.Length ?? 0).Should().BeLessThanOrEqualTo(AudioCodecColumnLength,
                    $"'{path}' with description '{description}' must fit Track.AudioCodec");
            }
        }
    }

    // BadgeResolver picks mp3.png/flac.png/aac.png and MediaDedupeManager scores
    // lossless vs lossy, both by substring. The prose form matched neither, so
    // even the tracks short enough to save got no badge and the wrong score.
    [Theory]
    [InlineData("/m/01.mp3", "mp3")]
    [InlineData("/m/01.flac", "flac")]
    [InlineData("/m/01.m4a", "aac")]
    public void Produces_a_token_the_badge_and_dedupe_matchers_recognise(string path, string expectedSubstring)
    {
        var codec = VoraLocalMediaScannerProvider.ResolveAudioCodec(path, null);

        codec.Should().NotBeNull();
        codec!.ToLowerInvariant().Should().Contain(expectedSubstring);
    }
}
