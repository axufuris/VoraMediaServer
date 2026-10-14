using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;
using static Vora.Infrastructure.Tests.Transcoding.TranscodeArgumentScenarios;

namespace Vora.Infrastructure.Tests.Transcoding;

public class KeyframeSegmentArgumentTests
{
    private const string UnfinishedSegments = "/transcode/heat_%d.part";
    private const string SegmentList = "/transcode/_ffmpeg_heat.list";
    private static readonly SegmentPlan Plan = SegmentPlan.FromKeyframes([0, 5, 10, 15], 20)!;

    private static PlaybackDecisionVM Remux() => new()
    {
        MediaItemId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Decision = StreamingState.Remux,
        VideoStrategy = "Copy",
        AudioStrategy = "Copy",
        SourceAudioCodec = "eac3",
        SelectedVideoStreamIndex = 0,
        SelectedAudioStreamIndex = 1,
    };

    private static string Build(SegmentLaunch launch) =>
        string.Join(" ", FFmpegTranscodeService.BuildFFmpegArguments(Source, UnfinishedSegments, SegmentList, Remux(), new ServerSetting(), hardware: null, launch, PlainHost));

    [Fact]
    public void Copied_video_from_the_start_is_cut_just_before_each_planned_keyframe()
    {
        Build(Plan.LaunchAt(0))
            .Should().Be("-i /media/movies/Heat (1995)/Heat.mkv -map 0:0 -map 0:1 -sn -c:v copy -c:a copy -output_ts_offset 1.000 -f segment -segment_format mpegts -segment_times 4.850,9.850,14.850 -segment_list /transcode/_ffmpeg_heat.list -segment_list_type flat -segment_start_number 0 -reset_timestamps 0 /transcode/heat_%d.part");
    }

    [Fact]
    public void A_restart_of_copied_video_seeks_just_past_its_keyframe_and_cuts_just_before_the_next_ones()
    {
        Build(Plan.LaunchAt(2))
            .Should().Be("-noaccurate_seek -ss 10.250 -i /media/movies/Heat (1995)/Heat.mkv -map 0:0 -map 0:1 -sn -c:v copy -c:a copy -output_ts_offset 11.250 -f segment -segment_format mpegts -segment_times 4.600 -segment_list /transcode/_ffmpeg_heat.list -segment_list_type flat -segment_start_number 2 -reset_timestamps 0 /transcode/heat_%d.part");
    }

    [Fact]
    public void The_last_segment_runs_to_the_end_without_a_cut()
    {
        Build(Plan.LaunchAt(3))
            .Should().Be("-noaccurate_seek -ss 15.250 -i /media/movies/Heat (1995)/Heat.mkv -map 0:0 -map 0:1 -sn -c:v copy -c:a copy -output_ts_offset 16.250 -f segment -segment_format mpegts -segment_list /transcode/_ffmpeg_heat.list -segment_list_type flat -segment_start_number 3 -reset_timestamps 0 /transcode/heat_%d.part");
    }
}
