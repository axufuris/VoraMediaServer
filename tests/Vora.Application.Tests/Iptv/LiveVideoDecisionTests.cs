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
    public void A_player_that_sent_nothing_is_assumed_to_take_the_modern_codecs_and_not_mpeg2()
    {
        var limits = LiveVideoLimits.Effective(None);

        limits.Should().BeSameAs(LiveVideoLimits.Default);
        LiveVideoDecision.Plays(H264("High 10", 51), limits).Should().BeTrue();
        LiveVideoDecision.Plays(new LiveVideoFormat("hevc", "Main 10", 153, 2160, 50, false), limits).Should().BeTrue();
        LiveVideoDecision.Plays(new LiveVideoFormat("mpeg2video", "Main", 8, 1080, 29.97, true), limits).Should().BeFalse();
        LiveVideoDecision.Plays(new LiveVideoFormat("mpeg4", "Simple", 1, 480, 25, false), limits).Should().BeFalse();
        LiveVideoDecision.Plays(new LiveVideoFormat("vc1", "Advanced", 3, 1080, 25, false), limits).Should().BeFalse();
    }

    [Fact]
    public void A_player_that_sent_limits_keeps_them_and_null_means_nothing_was_sent()
    {
        LiveVideoLimits.Effective(Roku).Should().BeSameAs(Roku);
        LiveVideoLimits.Effective(null).Should().BeSameAs(LiveVideoLimits.Default);
    }

    [Fact]
    public void An_encode_for_a_live_remux_carries_its_shorter_segment_length()
    {
        var format = new LiveVideoFormat("mpeg2video", "Main", 8, 1080, 29.97, true);

        LiveVideoDecision.EncodeFor(format, LiveVideoLimits.Default).SegmentSeconds.Should().Be(4);
        LiveVideoDecision.EncodeFor(format, LiveVideoLimits.Default, segmentSeconds: 2).SegmentSeconds.Should().Be(2);
        LiveVideoDecision.EncodeFor(format, LiveVideoLimits.Default).Deinterlace.Should().BeTrue();
    }

    [Fact]
    public void An_encode_of_a_format_whose_scan_type_is_known_deinterlaces_all_frames_or_none()
    {
        var interlaced = LiveVideoDecision.EncodeFor(new LiveVideoFormat("mpeg2video", "Main", 8, 1080, 29.97, true), LiveVideoLimits.Default);
        var progressive = LiveVideoDecision.EncodeFor(new LiveVideoFormat("mpeg2video", "Main", 4, 720, 59.94, false), LiveVideoLimits.Default);

        interlaced.Should().BeEquivalentTo(new { Deinterlace = true, OnlyFlaggedFrames = false });
        progressive.Should().BeEquivalentTo(new { Deinterlace = false, OnlyFlaggedFrames = false });
    }

    [Fact]
    public void An_encode_of_a_channel_known_only_by_its_codec_deinterlaces_the_frames_flagged_interlaced()
    {
        var encode = LiveVideoDecision.EncodeFor(new LiveVideoFormat("mpeg2video", null, null, null, null, null), LiveVideoLimits.Default);

        encode.Should().BeEquivalentTo(new { Level = 41, MaxHeight = (int?)1080, Deinterlace = true, OnlyFlaggedFrames = true });
    }

    [Fact]
    public void A_channel_known_only_by_its_codec_is_judged_by_that_codec()
    {
        var codecOnly = new LiveVideoFormat("mpeg2video", null, null, null, null, null);

        LiveVideoDecision.Plays(codecOnly, LiveVideoLimits.Default).Should().BeFalse();
        LiveVideoDecision.Plays(codecOnly, new LiveVideoLimits(["h264", "mpeg2video"], null, null)).Should().BeTrue();
        LiveVideoDecision.Plays(new LiveVideoFormat("h264", null, null, null, null, null), Roku).Should().BeTrue("with no profile or level to judge, only the codec counts");
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
