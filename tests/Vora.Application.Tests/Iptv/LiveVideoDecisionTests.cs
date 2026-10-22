using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class LiveVideoDecisionTests
{
    private static readonly LiveVideoLimits Roku = new(["h264", "hevc", "mpeg4"], 42, ["baseline", "main", "high"]);
    private static readonly LiveVideoLimits None = new(null, null, null);

    private static LiveVideoFormat H264(string? profile, int? level, int height = 1080, double frameRate = 25, bool interlaced = false) =>
        new("h264", profile, level, height, frameRate, interlaced);

    [Fact]
    public void High_10_is_encoded_for_a_player_that_takes_only_baseline_main_and_high()
    {
        LiveVideoDecision.Plays(H264("High 10", 41), Roku).Should().BeFalse();
        LiveVideoDecision.Mismatch(H264("High 10", 41), Roku).Should().Be("the player doesn't take H.264 High 10");
    }

    [Fact]
    public void Level_5_1_is_encoded_for_a_player_that_stops_at_4_2()
    {
        LiveVideoDecision.Plays(H264("High", 51), Roku).Should().BeFalse();
        LiveVideoDecision.Mismatch(H264("High", 51), Roku).Should().Be("H.264 level 5.1 is above the player's 4.2");
    }

    [Theory]
    [InlineData("High", 41)]
    [InlineData("High", 42)]
    [InlineData("Main", 40)]
    [InlineData("Constrained Baseline", 31)]
    public void A_stream_inside_the_players_limits_is_copied(string profile, int level)
    {
        LiveVideoDecision.Plays(H264(profile, level), Roku).Should().BeTrue();
    }

    [Fact]
    public void A_player_that_sends_no_limits_gets_a_copy_of_anything()
    {
        None.IsEmpty.Should().BeTrue();
        LiveVideoDecision.Plays(H264("High 10", 51), None).Should().BeTrue();
        LiveVideoDecision.Plays(new LiveVideoFormat("mpeg2video", "Main", 8, 576, 25, true), None).Should().BeTrue();
    }

    [Fact]
    public void A_codec_the_player_doesnt_list_is_encoded()
    {
        LiveVideoDecision.Mismatch(new LiveVideoFormat("mpeg2video", "Main", 8, 576, 25, true), Roku).Should().Be("the player doesn't take mpeg2video");
    }

    [Fact]
    public void Profile_and_level_only_judge_h264()
    {
        LiveVideoDecision.Plays(new LiveVideoFormat("hevc", "Main 10", 153, 2160, 50, false), Roku).Should().BeTrue();
    }

    [Fact]
    public void Profiles_are_compared_by_name_whatever_their_case_and_constrained_baseline_counts_as_baseline()
    {
        LiveVideoDecision.NormaliseProfile("High 10").Should().Be("high 10");
        LiveVideoDecision.NormaliseProfile("Constrained Baseline").Should().Be("baseline");
        LiveVideoDecision.Plays(H264("HIGH", 40), new LiveVideoLimits(null, null, ["High"])).Should().BeTrue();
        LiveVideoDecision.Plays(H264("Constrained Baseline", 30), new LiveVideoLimits(null, null, ["baseline"])).Should().BeTrue();
    }

    [Fact]
    public void A_profile_or_level_ffprobe_couldnt_read_doesnt_force_an_encode()
    {
        LiveVideoDecision.Plays(H264(null, null), Roku).Should().BeTrue();
    }

    [Fact]
    public void The_encode_is_level_4_1_at_up_to_30_frames_a_second_and_4_2_above()
    {
        LiveVideoDecision.EncodeFor(H264("High 10", 51, frameRate: 25), Roku).Level.Should().Be(41);
        LiveVideoDecision.EncodeFor(H264("High 10", 51, frameRate: 29.97), Roku).Level.Should().Be(41);
        LiveVideoDecision.EncodeFor(H264("High 10", 51, frameRate: 50), Roku).Level.Should().Be(42);
        LiveVideoDecision.EncodeFor(H264("High 10", 51, frameRate: 59.94), Roku).Level.Should().Be(42);
    }

    [Fact]
    public void The_encode_never_goes_above_the_players_own_level()
    {
        LiveVideoDecision.EncodeFor(H264("High 10", 51, frameRate: 50), Roku with { MaxH264Level = 41 }).Level.Should().Be(41);
    }

    [Fact]
    public void A_picture_taller_than_1080_is_scaled_down_and_one_that_fits_is_left_alone()
    {
        LiveVideoDecision.EncodeFor(H264("High 10", 51, height: 2160), Roku).MaxHeight.Should().Be(1080);
        LiveVideoDecision.EncodeFor(H264("High 10", 51, height: 1080), Roku).MaxHeight.Should().BeNull();
        LiveVideoDecision.EncodeFor(H264("High 10", 51, height: 576), Roku).MaxHeight.Should().BeNull();
    }

    [Fact]
    public void An_interlaced_picture_is_deinterlaced_and_a_progressive_one_is_not()
    {
        LiveVideoDecision.EncodeFor(H264("High 10", 51, interlaced: true), Roku).Deinterlace.Should().BeTrue();
        LiveVideoDecision.EncodeFor(H264("High 10", 51, interlaced: false), Roku).Deinterlace.Should().BeFalse();
    }
}
