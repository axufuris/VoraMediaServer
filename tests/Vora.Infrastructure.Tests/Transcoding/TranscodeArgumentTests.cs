using Vora.Domain.Enums;
using static Vora.Infrastructure.Tests.Transcoding.TranscodeArgumentScenarios;

namespace Vora.Infrastructure.Tests.Transcoding;

public class TranscodeArgumentTests
{
    private const string HlsTail = " -c:a copy -f hls -hls_time 4 -hls_playlist_type vod -hls_list_size 0 -hls_flags independent_segments+temp_file -hls_segment_filename /transcode/heat_%d.ts -start_number 0 /transcode/_ffmpeg_heat.m3u8";
    private const string Input = "-i /media/movies/Heat (1995)/Heat.mkv -map 0:0 -map 0:1 -sn";
    private const string Keyframes = "-force_key_frames expr:gte(t,n_forced*4)";
    private const string Vaapi = "-init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va";
    private const string QuickSync = "-init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device qsv=qs@va -filter_hw_device va";
    private const string BurnIn = "subtitles='/media/movies/Heat (1995)/Heat.mkv'";

    [Fact]
    public void A_cpu_transcode_encodes_with_x264()
    {
        Build(Transcode(), hardware: null, JellyfinHost)
            .Should().Be($"{Input} -c:v libx264 -preset veryfast -pix_fmt yuv420p {Keyframes}" + HlsTail);
    }

    [Fact]
    public void Nvidia_decoding_with_a_cpu_encode_decodes_with_cuda()
    {
        Build(Transcode(), DecodeOn(Nvidia), JellyfinHost)
            .Should().Be($"-hwaccel cuda {Input} -c:v libx264 -preset veryfast -pix_fmt yuv420p {Keyframes}" + HlsTail);
    }

    [Fact]
    public void Nvidia_keeps_frames_on_the_gpu_from_decode_to_nvenc()
    {
        Build(Transcode(), EncodeOn(Nvidia), JellyfinHost)
            .Should().Be($"-hwaccel cuda -hwaccel_output_format cuda -filter_threads 0 -filter_complex_threads 0 {Input} -c:v h264_nvenc -preset p4 -tune hq {Keyframes} -vf scale_cuda=iw:ih:format=nv12" + HlsTail);
    }

    [Fact]
    public void Nvidia_hevc_scales_on_the_gpu_and_caps_the_bitrate()
    {
        Build(Transcode(VideoCodec.Hevc, outputHeight: 1080, bandwidthKbps: 8000), EncodeOn(Nvidia with { Device = "0" }), JellyfinHost)
            .Should().Be($"-hwaccel cuda -hwaccel_output_format cuda -hwaccel_device 0 -filter_threads 0 -filter_complex_threads 0 {Input} -c:v hevc_nvenc -preset p4 -tune hq -rc vbr -cq 28 -qmin 28 -qmax 34 {Keyframes} -vf scale_cuda=-2:1080:format=p010le -maxrate 8000k -bufsize 480000k" + HlsTail);
    }

    [Fact]
    public void Nvidia_tonemaps_hdr_with_cuda_on_jellyfin_ffmpeg()
    {
        Build(Transcode(hdr: "HDR10", outputHeight: 1080), EncodeOn(Nvidia), JellyfinHost)
            .Should().Be($"-hwaccel cuda -hwaccel_output_format cuda -filter_threads 0 -filter_complex_threads 0 {Input} -c:v h264_nvenc -preset p4 -tune hq {Keyframes} -vf scale_cuda=-2:1080:format=p010le,tonemap_cuda=tonemap=hable:format=nv12:peak=1000" + HlsTail);
    }

    [Fact]
    public void Nvidia_without_a_gpu_tonemap_downloads_and_tonemaps_on_the_cpu()
    {
        Build(Transcode(hdr: "HDR10"), EncodeOn(Nvidia), PlainHost)
            .Should().Be($"-hwaccel cuda -hwaccel_output_format cuda -filter_threads 0 -filter_complex_threads 0 {Input} -c:v h264_nvenc -preset p4 -tune hq {Keyframes} -vf scale_cuda=-2:1080:format=p010le,hwdownload,format=p010le,zscale=p=bt709:t=bt709:m=bt709:r=tv:d=ordered,format=yuv420p" + HlsTail);
    }

    [Fact]
    public void A_cpu_transcode_tonemaps_hdr_with_zscale()
    {
        Build(Transcode(hdr: "HDR10", outputHeight: 1080), hardware: null, JellyfinHost)
            .Should().Be($"{Input} -c:v libx264 -preset veryfast -pix_fmt yuv420p {Keyframes} -vf scale=-2:1080,zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p" + HlsTail);
    }

    [Fact]
    public void Burned_in_subtitles_share_one_filter_chain_with_the_scale_on_nvidia()
    {
        Build(Transcode(outputHeight: 720, burnIn: true), EncodeOn(Nvidia), JellyfinHost)
            .Should().Be($"-hwaccel cuda {Input} -c:v h264_nvenc -preset p4 -tune hq -pix_fmt yuv420p {Keyframes} -vf scale=-2:720,{BurnIn}" + HlsTail);
    }

    [Fact]
    public void Burned_in_subtitles_share_one_filter_chain_with_the_scale_on_the_cpu()
    {
        Build(Transcode(outputHeight: 720, burnIn: true), hardware: null, JellyfinHost)
            .Should().Be($"{Input} -c:v libx264 -preset veryfast -pix_fmt yuv420p {Keyframes} -vf scale=-2:720,{BurnIn}" + HlsTail);
    }

    [Fact]
    public void A_remux_after_a_seek_copies_video_without_touching_the_gpu()
    {
        Build(DirectStream(), EncodeOn(Nvidia), JellyfinHost, startSegment: 10)
            .Should().Be("-ss 40.000 -i /media/movies/Heat (1995)/Heat.mkv -map 0:0 -map 0:2 -sn -c:v copy -c:a ac3 -ac 6 -b:a 384k -output_ts_offset 40.000 -f hls -hls_time 4 -hls_playlist_type vod -hls_list_size 0 -hls_flags independent_segments+temp_file -hls_segment_filename /transcode/heat_%d.ts -start_number 10 /transcode/_ffmpeg_heat.m3u8");
    }

    [Fact]
    public void Quick_sync_decodes_with_vaapi_and_maps_the_frames_to_the_qsv_encoder()
    {
        Build(Transcode(), EncodeOn(IntelQuickSync), JellyfinHost)
            .Should().Be($"{QuickSync} -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v h264_qsv -preset medium -forced_idr 1 -global_quality 23 {Keyframes} -vf scale_vaapi=format=nv12,hwmap=derive_device=qsv,format=qsv" + HlsTail);
    }

    [Fact]
    public void Quick_sync_tonemaps_hdr_on_the_intel_gpu_and_caps_the_bitrate()
    {
        Build(Transcode(hdr: "HDR10", outputHeight: 1080, bandwidthKbps: 8000), EncodeOn(IntelQuickSync), PlainHost)
            .Should().Be($"{QuickSync} -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v h264_qsv -preset medium -forced_idr 1 {Keyframes} -vf scale_vaapi=w=-2:h=1080,tonemap_vaapi=format=nv12:p=bt709:t=bt709:m=bt709,hwmap=derive_device=qsv,format=qsv -b:v 8000k -maxrate 8000k -bufsize 480000k" + HlsTail);
    }

    [Fact]
    public void Vaapi_scales_and_encodes_hevc_on_the_gpu()
    {
        Build(Transcode(VideoCodec.Hevc, outputHeight: 720), EncodeOn(IntelVaapi), JellyfinHost)
            .Should().Be($"{Vaapi} -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v hevc_vaapi {Keyframes} -vf scale_vaapi=w=-2:h=720:format=nv12" + HlsTail);
    }

    [Fact]
    public void Amd_tonemaps_hdr_through_vulkan_when_the_gpu_supports_it()
    {
        Build(Transcode(hdr: "HDR10"), EncodeOn(AmdVaapiWithVulkan), PlainHost)
            .Should().Be($"-init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device vulkan=vk@va -filter_hw_device va -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v h264_vaapi {Keyframes} -vf hwmap=derive_device=vulkan,format=vulkan,libplacebo=format=nv12:tonemapping=hable:colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv,hwmap=derive_device=vaapi,format=vaapi,scale_vaapi=format=nv12" + HlsTail);
    }

    [Fact]
    public void Amd_without_vulkan_tonemaps_on_the_cpu_between_gpu_decode_and_encode()
    {
        Build(Transcode(hdr: "HDR10"), EncodeOn(AmdVaapi), JellyfinHost)
            .Should().Be($"{Vaapi} -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v h264_vaapi {Keyframes} -vf scale_vaapi=w=-2:h=1080,hwdownload,format=p010le,zscale=p=bt709:t=bt709:m=bt709:r=tv:d=ordered,format=yuv420p,format=nv12,hwupload" + HlsTail);
    }

    [Fact]
    public void Quick_sync_burns_subtitles_in_on_the_cpu_and_encodes_on_the_gpu()
    {
        Build(Transcode(outputHeight: 720, burnIn: true), EncodeOn(IntelQuickSync), JellyfinHost)
            .Should().Be($"{QuickSync} -hwaccel vaapi {Input} -c:v h264_qsv -preset medium -forced_idr 1 -global_quality 23 {Keyframes} -vf scale=-2:720,{BurnIn},format=nv12" + HlsTail);
    }

    [Fact]
    public void Vaapi_uploads_the_cpu_frames_after_burning_subtitles_in()
    {
        Build(Transcode(outputHeight: 720, burnIn: true), EncodeOn(AmdVaapi), JellyfinHost)
            .Should().Be($"{Vaapi} -hwaccel vaapi {Input} -c:v h264_vaapi {Keyframes} -vf scale=-2:720,{BurnIn},format=nv12,hwupload" + HlsTail);
    }

    [Fact]
    public void Vaapi_decoding_with_a_cpu_encode_hands_frames_to_x264()
    {
        Build(Transcode(), DecodeOn(IntelVaapi), JellyfinHost)
            .Should().Be($"{Vaapi} -hwaccel vaapi {Input} -c:v libx264 -preset veryfast -pix_fmt yuv420p {Keyframes}" + HlsTail);
    }
}
