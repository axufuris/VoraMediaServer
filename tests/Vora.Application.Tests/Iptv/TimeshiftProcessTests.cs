using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Tests.Iptv;

public class TimeshiftProcessTests
{
    private const string StreamUrl = "https://iptv.example.com/action24.ts";
    private const string Playlist = "/transcode/timeshift/p/s/index.m3u8";
    private const string Segments = "/transcode/timeshift/p/s/seg_%03d.ts";

    private static List<string> Arguments(TimeshiftVideoArguments video)
    {
        using var process = IptvManager.BuildTimeshiftProcess(StreamUrl, Playlist, Segments, video);
        return [.. process.StartInfo.ArgumentList];
    }

    [Fact]
    public void A_copy_runs_exactly_the_command_timeshift_has_always_run()
    {
        Arguments(TimeshiftVideoArguments.Copy).Should().Equal(
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
            "-i", StreamUrl,
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "128k",
            "-f", "hls",
            "-hls_time", "4",
            "-hls_list_size", "0",
            "-hls_flags", "append_list+temp_file",
            "-hls_segment_filename", Segments,
            Playlist);
    }

    [Fact]
    public void An_encode_puts_the_device_setup_before_the_input_and_the_video_settings_after_it()
    {
        var video = new TimeshiftVideoArguments(
            ["-init_hw_device", "vaapi=va:/dev/dri/renderD128", "-filter_hw_device", "va"],
            ["-vf", "format=nv12,hwupload", "-c:v", "h264_vaapi", "-qp", "28", "-profile:v", "high", "-level", "41", "-force_key_frames", "expr:gte(t,n_forced*4)"],
            "VAAPI",
            OnGpu: true);

        Arguments(video).Should().Equal(
            "-init_hw_device", "vaapi=va:/dev/dri/renderD128", "-filter_hw_device", "va",
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
            "-i", StreamUrl,
            "-vf", "format=nv12,hwupload", "-c:v", "h264_vaapi", "-qp", "28", "-profile:v", "high", "-level", "41", "-force_key_frames", "expr:gte(t,n_forced*4)",
            "-c:a", "aac",
            "-b:a", "128k",
            "-f", "hls",
            "-hls_time", "4",
            "-hls_list_size", "0",
            "-hls_flags", "append_list+temp_file",
            "-hls_segment_filename", Segments,
            Playlist);
    }

    [Fact]
    public void A_channel_that_was_never_watched_has_no_stored_format()
    {
        var channel = new IptvChannel { ExternalChannelId = "action24", Name = "Action 24", StreamUrl = StreamUrl };

        IptvManager.StoredVideoFormat(channel).Should().BeNull();
    }

    [Fact]
    public void A_watched_channel_brings_back_the_format_its_last_session_saw()
    {
        var channel = new IptvChannel
        {
            ExternalChannelId = "action24",
            Name = "Action 24",
            StreamUrl = StreamUrl,
            VideoCodec = "h264",
            VideoProfile = "High 10",
            VideoLevel = 51,
            VideoHeight = 1080,
            VideoFrameRate = 25,
        };

        IptvManager.StoredVideoFormat(channel).Should().Be(new LiveVideoFormat("h264", "High 10", 51, 1080, 25, false));
    }
}
