using Vora.Infrastructure.Analysis;

namespace Vora.Infrastructure.Tests;

public class FFmpegAnalyzerHdr10PlusTests
{
    [Fact]
    public void Detects_hdr10plus_from_smpte2094_40_frame_side_data()
    {
        var json = """
        {
            "frames": [
                {
                    "side_data_list": [
                        { "side_data_type": "H.26[45] User Data Unregistered SEI message" },
                        { "side_data_type": "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)" }
                    ]
                }
            ]
        }
        """;

        Assert.True(FFmpegAnalyzerService.Hdr10PlusJsonIndicatesDynamicMetadata(json));
    }

    [Fact]
    public void Does_not_flag_hdr10plus_for_dolby_vision_or_static_hdr10_side_data()
    {
        var json = """
        {
            "frames": [
                {
                    "side_data_list": [
                        { "side_data_type": "Dolby Vision RPU Data" },
                        { "side_data_type": "Mastering display metadata" },
                        { "side_data_type": "Content light level metadata" }
                    ]
                }
            ]
        }
        """;

        Assert.False(FFmpegAnalyzerService.Hdr10PlusJsonIndicatesDynamicMetadata(json));
    }

    [Fact]
    public void Handles_frames_without_side_data()
    {
        var json = """{ "frames": [ { "pict_type": "I" }, { } ] }""";

        Assert.False(FFmpegAnalyzerService.Hdr10PlusJsonIndicatesDynamicMetadata(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    public void Returns_false_for_empty_or_malformed_output(string? json)
    {
        Assert.False(FFmpegAnalyzerService.Hdr10PlusJsonIndicatesDynamicMetadata(json));
    }
}
