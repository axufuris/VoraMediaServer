using System.Text.Json;
using Vora.Infrastructure.Analysis;

namespace Vora.Infrastructure.Tests;

public class HdrClassifierTests
{
    private static string Stream(string? colorTransfer, int? dolbyVisionCompatibility, int dolbyVisionProfile = 8)
    {
        var transfer = colorTransfer is null ? "" : $"\"color_transfer\": \"{colorTransfer}\",";
        var sideData = dolbyVisionCompatibility is null
            ? ""
            : $$"""
              ,"side_data_list": [
                  {
                      "side_data_type": "DOVI configuration record",
                      "dv_version_major": 1,
                      "dv_version_minor": 0,
                      "dv_profile": {{dolbyVisionProfile}},
                      "dv_level": 6,
                      "rpu_present_flag": 1,
                      "el_present_flag": 0,
                      "bl_present_flag": 1,
                      "dv_bl_signal_compatibility_id": {{dolbyVisionCompatibility}}
                  }
              ]
              """;
        return $$"""
            {
                "index": 0,
                "codec_name": "hevc",
                "codec_type": "video",
                "pix_fmt": "yuv420p10le",
                {{transfer}}
                "color_range": "tv"
                {{sideData}}
            }
            """;
    }

    private static string? Classify(string json)
    {
        using var document = JsonDocument.Parse(json);
        return HdrClassifier.Classify(document.RootElement);
    }

    [Fact]
    public void A_dolby_vision_record_on_a_picture_tagged_bt709_is_standard_dynamic_range()
    {
        Classify(Stream("bt709", dolbyVisionCompatibility: 1)).Should().BeNull();
    }

    [Theory]
    [InlineData("smpte2084", null, "HDR10")]
    [InlineData("smpte2084", 1, "DoVi/HDR10")]
    [InlineData("arib-std-b67", null, "HLG")]
    [InlineData("arib-std-b67", 4, "DoVi/HLG")]
    [InlineData("bt709", null, null)]
    [InlineData(null, null, null)]
    [InlineData("bt709", 2, null)]
    [InlineData("smpte170m", 1, null)]
    [InlineData("bt2020-10", 1, null)]
    public void Labels_the_picture_by_its_transfer_and_any_dolby_vision_it_carries(string? transfer, int? compatibility, string? expected)
    {
        Classify(Stream(transfer, compatibility)).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("bt709")]
    public void Profile_5_has_no_picture_without_dolby_vision_whatever_its_tags_say(string? transfer)
    {
        Classify(Stream(transfer, dolbyVisionCompatibility: 0, dolbyVisionProfile: 5)).Should().Be("DoVi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public void Dolby_vision_on_an_untagged_picture_stays_dolby_vision(string? transfer)
    {
        Classify(Stream(transfer, dolbyVisionCompatibility: 1)).Should().Be("DoVi");
    }

    [Fact]
    public void A_dolby_vision_record_without_a_compatibility_id_is_not_taken_for_standard_dynamic_range()
    {
        HdrClassifier.Classify("bt709", new DolbyVisionRecord(BaseLayerCompatibility: null)).Should().Be("DoVi");
    }

    [Theory]
    [InlineData("DoVi", true)]
    [InlineData(" dovi ", true)]
    [InlineData("DoVi/HDR10", false)]
    [InlineData("DoVi/HLG", false)]
    [InlineData("HDR10", false)]
    [InlineData(null, false)]
    public void Only_a_bare_dolby_vision_label_is_rechecked_before_a_transcode(string? hdrType, bool recheck)
    {
        HdrClassifier.HasNoKnownBaseLayer(hdrType).Should().Be(recheck);
    }

    [Fact]
    public void Reads_the_first_stream_of_a_probe()
    {
        var probe = HdrClassifier.ReadProbe($$"""{ "streams": [ {{Stream("bt709", dolbyVisionCompatibility: 1)}} ] }""");

        probe.Should().Be(new HdrProbeResult(HdrType: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "streams": [] }""")]
    public void A_probe_that_says_nothing_keeps_the_scanned_label(string? json)
    {
        HdrClassifier.ReadProbe(json).Should().BeNull();
    }
}
