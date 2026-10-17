using System.Text.Json;
using Vora.Application.Analysis.Results;
using Vora.Infrastructure.Analysis;

namespace Vora.Infrastructure.Tests;

public class VideoGeometryTests
{
    private static VideoGeometryReading Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return VideoGeometry.Read(document.RootElement);
    }

    [Fact]
    public void Reads_the_size_and_the_average_frame_rate()
    {
        Read("""{ "width": 3840, "height": 1540, "avg_frame_rate": "24000/1001", "r_frame_rate": "24000/1001" }""")
            .Should().Be(new VideoGeometryReading(3840, 1540, 23.976));
    }

    [Fact]
    public void Falls_back_to_the_stream_rate_when_the_average_is_not_known()
    {
        Read("""{ "width": 1920, "height": 1080, "avg_frame_rate": "0/0", "r_frame_rate": "60/1" }""")
            .Should().Be(new VideoGeometryReading(1920, 1080, 60));
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "width": 0, "height": 0, "avg_frame_rate": "0/0" }""")]
    [InlineData("""{ "width": "wide", "avg_frame_rate": "abc" }""")]
    [InlineData("""{ "avg_frame_rate": "90000/1" }""")]
    public void Leaves_out_what_the_probe_did_not_give(string json)
    {
        Read(json).Should().Be(new VideoGeometryReading(null, null, null));
    }

    [Fact]
    public void Reads_each_video_stream_and_its_dynamic_range_but_not_the_cover_art()
    {
        var json = """
            {
                "streams": [
                    { "index": 0, "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 1540, "avg_frame_rate": "24000/1001", "color_transfer": "bt709",
                      "side_data_list": [ { "side_data_type": "DOVI configuration record", "dv_profile": 8, "dv_bl_signal_compatibility_id": 1 } ] },
                    { "index": 3, "codec_type": "video", "codec_name": "mjpeg", "width": 600, "height": 900, "disposition": { "attached_pic": 1 } }
                ]
            }
            """;

        FFmpegAnalyzerService.ReadVideoStreams(json).Should().Equal(new VideoStreamFacts(0, 3840, 1540, 23.976, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void A_probe_that_says_nothing_reads_as_no_streams(string? json)
    {
        FFmpegAnalyzerService.ReadVideoStreams(json).Should().BeEmpty();
    }
}
