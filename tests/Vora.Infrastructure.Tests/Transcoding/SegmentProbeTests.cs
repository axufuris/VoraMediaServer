using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class SegmentProbeTests
{
    private const string HevcWithEac3 = """
        {"streams":[
          {"codec_name":"hevc","profile":"Main 10","codec_type":"video","codec_tag_string":"[0][0][0][0]","width":1918,"height":816,"coded_width":1920,"coded_height":816,"pix_fmt":"yuv420p10le","level":120,"start_time":"1.400000"},
          {"codec_name":"eac3","codec_type":"audio","codec_tag_string":"[0][0][0][0]","sample_rate":"48000","channels":6,"start_time":"1.420000"}
        ]}
        """;

    [Fact]
    public void Describes_the_video_the_audio_and_how_far_apart_they_start()
    {
        var summary = SegmentProbe.Describe(HevcWithEac3);

        summary.Should().Be("video hevc Main 10 level 120 yuv420p10le 1918x816 coded 1920x816 start 1.400s; audio eac3 6ch 48000Hz start 1.420s; audio starts +0.020s from the video");
    }

    [Fact]
    public void Shows_a_codec_tag_only_when_there_is_a_real_one()
    {
        var json = """{"streams":[{"codec_type":"video","codec_name":"hevc","codec_tag_string":"hvc1","width":1920,"height":1080,"coded_width":1920,"coded_height":1080}]}""";

        SegmentProbe.Describe(json).Should().Be("video hevc tag hvc1 1920x1080");
    }

    [Fact]
    public void Shows_an_audio_start_that_is_before_the_video_as_negative()
    {
        var json = """{"streams":[{"codec_type":"video","codec_name":"h264","start_time":"2.000"},{"codec_type":"audio","codec_name":"aac","start_time":"1.900"}]}""";

        SegmentProbe.Describe(json).Should().EndWith("audio starts -0.100s from the video");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"streams":[]}""")]
    [InlineData("""{"streams":[{"codec_type":"subtitle","codec_name":"mov_text"}]}""")]
    public void Says_nothing_when_there_is_nothing_to_say(string? json)
    {
        SegmentProbe.Describe(json).Should().BeNull();
    }
}
