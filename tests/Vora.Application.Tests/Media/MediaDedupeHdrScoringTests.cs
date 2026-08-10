using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Media;

public class MediaDedupeHdrScoringTests
{
    private static MediaDedupeSettings Settings() => new()
    {
        ScoreHdrDolbyVision = 500,
        ScoreHdr = 300,
        ScoreHdr10PlusBonus = 100
    };

    [Fact]
    public void Dolby_vision_over_hdr10_scores_the_dolby_vision_points()
    {
        var (points, label) = MediaDedupeManager.ScoreHdr("DoVi/HDR10", "Movie [DV HDR10][Remux-2160p].mkv", Settings());

        Assert.Equal(500, points);
        Assert.Equal("DOVI/HDR10", label);
    }

    [Fact]
    public void Hdr10_plus_in_the_filename_adds_the_bonus_on_top_of_dolby_vision()
    {
        var (points, label) = MediaDedupeManager.ScoreHdr("DoVi/HDR10", "Movie [Hybrid][DV HDR10Plus][Remux-2160p].mkv", Settings());

        Assert.Equal(600, points);
        Assert.Equal("DOVI/HDR10+", label);
    }

    [Fact]
    public void Hdr10_plus_from_metadata_adds_the_bonus_without_a_filename_hint()
    {
        var (points, label) = MediaDedupeManager.ScoreHdr("HDR10Plus", "plain-name.mkv", Settings());

        Assert.Equal(400, points);
        Assert.Equal("HDR10PLUS", label);
    }

    [Fact]
    public void Plain_hdr10_with_no_plus_signal_gets_no_bonus()
    {
        var (points, label) = MediaDedupeManager.ScoreHdr("HDR10", "Movie [HDR10][Remux-2160p].mkv", Settings());

        Assert.Equal(300, points);
        Assert.Equal("HDR10", label);
    }

    [Fact]
    public void An_sdr_file_never_earns_the_bonus_even_if_the_filename_lies()
    {
        var (points, label) = MediaDedupeManager.ScoreHdr(null, "Movie [HDR10Plus but actually SDR].mkv", Settings());

        Assert.Equal(0, points);
        Assert.Equal("SDR", label);
    }

    [Fact]
    public void The_hdr10_plus_version_outscores_an_otherwise_identical_hdr10_version()
    {
        var settings = Settings();
        var hdr10 = MediaDedupeManager.ScoreHdr("DoVi/HDR10", "Anyone But You (2023) [Remux-2160p][DV HDR10][TrueHD Atmos 7.1][HEVC].mkv", settings);
        var hdr10Plus = MediaDedupeManager.ScoreHdr("DoVi/HDR10", "Anyone But You (2023) [Hybrid][Remux-2160p][DV HDR10Plus][TrueHD Atmos 7.1][HEVC].mkv", settings);

        Assert.True(hdr10Plus.Points > hdr10.Points);
    }
}
