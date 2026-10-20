using Vora.Application.Analysis.Results;
using Vora.Infrastructure.Analysis;

namespace Vora.Infrastructure.Tests;

public class StreamFactsReadingTests
{
    private const string Probe = """
        {
          "streams": [
            { "index": 0, "codec_type": "video", "codec_name": "hevc", "profile": "Main 10", "level": 153, "tags": { "BPS-eng": "14123456" } },
            { "index": 1, "codec_type": "audio", "codec_name": "dts", "profile": "DTS-HD MA", "sample_rate": "48000", "channels": 6, "channel_layout": "5.1(side)", "bit_rate": "3812000" },
            { "index": 2, "codec_type": "audio", "codec_name": "ac3", "sample_rate": "48000", "channel_layout": "stereo", "tags": { "BPS": "192000" } },
            { "index": 3, "codec_type": "subtitle", "codec_name": "hdmv_pgs_subtitle", "level": -99 }
          ]
        }
        """;

    [Fact]
    public void Reads_the_profile_level_sample_rate_layout_and_bitrate_of_every_stream()
    {
        FFmpegAnalyzerService.ReadStreamFacts(Probe).Should().Equal(
            new MediaStreamFacts(0, "video", "hevc", "Main 10", 153, null, null, 14_123_456),
            new MediaStreamFacts(1, "audio", "dts", "DTS-HD MA", null, 48000, "5.1(side)", 3_812_000),
            new MediaStreamFacts(2, "audio", "ac3", null, null, 48000, "stereo", 192_000),
            new MediaStreamFacts(3, "subtitle", "hdmv_pgs_subtitle", null, null, null, null, null));
    }

    [Theory]
    [InlineData("""{ "index": 0, "codec_type": "video", "tags": { "BPS": "23986068" } }""", 3_706_601L, null)]
    [InlineData("""{ "index": 0, "codec_type": "video", "tags": { "BPS-eng": "29800000" } }""", 35_000_000L, 29_800_000L)]
    [InlineData("""{ "index": 0, "codec_type": "video", "bit_rate": "9759000" }""", 11_167_000L, 9_759_000L)]
    [InlineData("""{ "index": 0, "codec_type": "video", "tags": { "BPS": "8000000" } }""", null, 8_000_000L)]
    [InlineData("""{ "index": 0, "codec_type": "video" }""", 11_167_000L, null)]
    public void A_video_bitrate_is_kept_only_when_it_fits_inside_the_whole_file(string stream, long? fileBitrate, long? expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(stream);

        FFmpegAnalyzerService.VideoTrackBitrate(document.RootElement, fileBitrate).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "format": {} }""")]
    public void Gives_nothing_for_a_probe_that_failed(string? json)
    {
        FFmpegAnalyzerService.ReadStreamFacts(json).Should().BeEmpty();
    }
}
