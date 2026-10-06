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

    [Fact]
    public void Reads_one_picture_sample_per_frame_from_the_metadata_print()
    {
        var lines = new[]
        {
            "[Parsed_blackdetect_0 @ 0x55d] black_start:6618.8 black_end:6650 black_duration:31.2",
            "[Parsed_metadata_5 @ 0x55e] frame:0    pts:6619    pts_time:6619",
            "[Parsed_metadata_5 @ 0x55e] lavfi.signalstats.SATAVG=0.48",
            "[Parsed_metadata_6 @ 0x55f] frame:0    pts:6619    pts_time:6619",
            "[Parsed_metadata_6 @ 0x55f] lavfi.signalstats.YLOW=16",
            "[Parsed_metadata_7 @ 0x560] frame:0    pts:6619    pts_time:6619",
            "[Parsed_metadata_7 @ 0x560] lavfi.signalstats.YAVG=25.7",
            "[Parsed_metadata_8 @ 0x561] frame:0    pts:6619    pts_time:6619",
            "[Parsed_metadata_8 @ 0x561] lavfi.signalstats.YMAX=231",
            "[Parsed_metadata_5 @ 0x55e] frame:1    pts:6620    pts_time:6620",
            "[Parsed_metadata_5 @ 0x55e] lavfi.signalstats.SATAVG=7.5"
        };
        var readings = new SortedDictionary<double, Dictionary<string, double>>();
        double? frameTime = null;

        foreach (var line in lines) FFmpegAnalyzerService.ReadPictureLine(line, readings, ref frameTime);
        var samples = FFmpegAnalyzerService.ToPictureSamples(readings);

        var sample = Assert.Single(samples);
        Assert.Equal(TimeSpan.FromSeconds(6619), sample.Time);
        Assert.Equal(0.48, sample.Saturation);
        Assert.Equal(16, sample.LowLuma);
        Assert.Equal(25.7, sample.MeanLuma);
        Assert.Equal(231, sample.PeakLuma);
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
