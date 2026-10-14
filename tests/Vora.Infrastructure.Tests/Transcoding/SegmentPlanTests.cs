using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class SegmentPlanTests
{
    private static readonly Guid MediaItem = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void An_encoded_stream_keeps_even_four_second_segments()
    {
        var plan = SegmentPlan.Uniform(10);

        plan.FollowsKeyframes.Should().BeFalse();
        plan.Count.Should().Be(3);
        Enumerable.Range(0, plan.Count).Select(plan.DurationOf).Should().Equal(4, 4, 2);
        plan.TargetDuration.Should().Be(4);
        plan.IndexAt(9.9).Should().Be(2);
    }

    [Fact]
    public void An_even_plan_launches_where_it_always_did()
    {
        SegmentPlan.Uniform(100).LaunchAt(10).Should().Be(new SegmentLaunch(10, 40, 40, null));
        SegmentPlan.Uniform(100).LaunchAt(0).Seeks.Should().BeFalse();
    }

    [Fact]
    public void A_duration_that_ends_on_a_boundary_does_not_add_an_empty_segment()
    {
        var plan = SegmentPlan.Uniform(8.005);

        plan.Count.Should().Be(3);
        plan.DurationOf(2).Should().Be(4);
    }

    [Fact]
    public void Copied_video_is_cut_at_the_first_keyframe_four_seconds_after_the_last_cut()
    {
        var plan = SegmentPlan.FromKeyframes([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 12);

        plan.Should().NotBeNull();
        plan!.FollowsKeyframes.Should().BeTrue();
        Enumerable.Range(0, plan.Count).Select(plan.StartOf).Should().Equal(0, 4, 8);
    }

    [Fact]
    public void A_long_gap_between_keyframes_makes_a_longer_segment()
    {
        var plan = SegmentPlan.FromKeyframes([0, 10, 20, 30], 35);

        Enumerable.Range(0, plan!.Count).Select(plan.DurationOf).Should().Equal(10, 10, 10, 5);
        plan.TargetDuration.Should().Be(10);
    }

    [Fact]
    public void A_keyframe_with_another_close_beside_it_is_never_a_cut_so_a_cut_or_restart_cannot_land_on_the_wrong_one()
    {
        var plan = SegmentPlan.FromKeyframes([0, 4, 4.1, 8, 12], 16);

        Enumerable.Range(0, plan!.Count).Select(plan.StartOf).Should().Equal(0, 8, 12);
    }

    [Fact]
    public void Keyframes_too_close_together_to_cut_between_fall_back_to_even_segments()
    {
        var everyQuarterSecond = Enumerable.Range(0, 120).Select(index => index * 0.25).ToArray();

        SegmentPlan.FromKeyframes(everyQuarterSecond, 30).Should().BeNull();
    }

    [Fact]
    public void One_burst_of_close_keyframes_only_lengthens_the_segment_around_it()
    {
        var plan = SegmentPlan.FromKeyframes([0, 4, 4.1, 4.2, 4.3, 9, 13, 17, 21, 25], 30);

        Enumerable.Range(0, plan!.Count).Select(plan.StartOf).Should().Equal(0, 9, 13, 17, 21, 25);
    }

    [Fact]
    public void Repeated_keyframe_times_count_once()
    {
        var plan = SegmentPlan.FromKeyframes([0, 0, 5, 5, 10], 15);

        Enumerable.Range(0, plan!.Count).Select(plan.StartOf).Should().Equal(0, 5, 10);
    }

    [Fact]
    public void A_restart_seeks_just_past_its_keyframe_and_keeps_the_timeline_of_the_first_run()
    {
        var plan = SegmentPlan.FromKeyframes([0, 5, 10, 15, 20], 25)!;

        var first = plan.LaunchAt(0);
        var restart = plan.LaunchAt(2);

        first.Should().Be(new SegmentLaunch(0, 0, 1.0, first.CutTimes));
        first.CutTimes!.Select(time => Math.Round(time, 6)).Should().Equal(4.85, 9.85, 14.85, 19.85);
        restart.SeekSeconds.Should().BeApproximately(10.25, 1e-9);
        restart.TimestampOffsetSeconds.Should().BeApproximately(11.25, 1e-9);
        restart.CutTimes!.Select(time => Math.Round(time, 6)).Should().Equal(4.6, 9.6);
    }

    [Fact]
    public void The_last_segment_has_nothing_left_to_cut()
    {
        var plan = SegmentPlan.FromKeyframes([0, 5, 10], 15)!;

        plan.LaunchAt(plan.Count - 1).CutTimes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(3)]
    public void A_file_whose_first_keyframe_is_late_falls_back_to_even_segments(double firstKeyframe)
    {
        SegmentPlan.FromKeyframes([firstKeyframe, 10, 20], 30).Should().BeNull();
    }

    [Fact]
    public void A_plan_without_keyframes_or_duration_falls_back_to_even_segments()
    {
        SegmentPlan.FromKeyframes([], 30).Should().BeNull();
        SegmentPlan.FromKeyframes([0, 5], 0).Should().BeNull();
    }

    [Fact]
    public void No_cut_is_placed_in_the_last_half_second()
    {
        var plan = SegmentPlan.FromKeyframes([0, 5, 9.8], 10)!;

        Enumerable.Range(0, plan.Count).Select(plan.StartOf).Should().Equal(0, 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4.99, 0)]
    [InlineData(5, 1)]
    [InlineData(24, 4)]
    [InlineData(99, 4)]
    [InlineData(-3, 0)]
    public void A_position_maps_to_the_segment_that_contains_it(double seconds, int segment)
    {
        SegmentPlan.FromKeyframes([0, 5, 10, 15, 20], 25)!.IndexAt(seconds).Should().Be(segment);
    }

    [Fact]
    public void The_playlist_lists_every_planned_segment_with_its_own_length()
    {
        var plan = SegmentPlan.FromKeyframes([0, 10, 22.5], 30)!;

        var playlist = FFmpegTranscodeService.VodPlaylist(MediaItem, plan, startSegment: 1);

        playlist.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "#EXTM3U",
            "#EXT-X-VERSION:6",
            "#EXT-X-TARGETDURATION:13",
            "#EXT-X-PLAYLIST-TYPE:VOD",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXT-X-INDEPENDENT-SEGMENTS",
            "#EXT-X-START:TIME-OFFSET=10.000,PRECISE=YES",
            "#EXTINF:10.000,", $"{MediaItem}_0.ts",
            "#EXTINF:12.500,", $"{MediaItem}_1.ts",
            "#EXTINF:7.500,", $"{MediaItem}_2.ts",
            "#EXT-X-ENDLIST");
    }

    [Fact]
    public void An_even_playlist_is_written_exactly_as_before()
    {
        var playlist = FFmpegTranscodeService.VodPlaylist(MediaItem, SegmentPlan.Uniform(9), startSegment: 0);

        playlist.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "#EXTM3U",
            "#EXT-X-VERSION:6",
            "#EXT-X-TARGETDURATION:4",
            "#EXT-X-PLAYLIST-TYPE:VOD",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXT-X-INDEPENDENT-SEGMENTS",
            "#EXTINF:4.000,", $"{MediaItem}_0.ts",
            "#EXTINF:4.000,", $"{MediaItem}_1.ts",
            "#EXTINF:1.000,", $"{MediaItem}_2.ts",
            "#EXT-X-ENDLIST");
    }
}
