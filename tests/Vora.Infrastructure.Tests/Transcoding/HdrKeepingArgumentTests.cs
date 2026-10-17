using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;
using static Vora.Infrastructure.Tests.Transcoding.TranscodeArgumentScenarios;

namespace Vora.Infrastructure.Tests.Transcoding;

public class HdrKeepingArgumentTests
{
    private const string Input = "-i /media/movies/Heat (1995)/Heat.mkv -map 0:0 -map 0:1 -sn";
    private const string Keyframes = "-force_key_frames expr:gte(t,n_forced*4)";
    private const string HlsTail = " -c:a copy -f hls -hls_time 4 -hls_playlist_type vod -hls_list_size 0 -hls_flags independent_segments+temp_file -hls_segment_filename /transcode/heat_%d.ts -start_number 0 /transcode/_ffmpeg_heat.m3u8";
    private const string Hdr10Out = "-profile:v main10 -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc -color_range tv";

    private static PlaybackDecisionVM KeepingHdr(string hdr = "HDR10")
    {
        var decision = Transcode(VideoCodec.Hevc, hdr: hdr, outputHeight: 1080, bandwidthKbps: 8000);
        decision.OutputHdrType = hdr;
        return decision;
    }

    [Fact]
    public void Nvidia_keeps_hdr10_as_10_bit_hevc_without_tone_mapping()
    {
        Build(KeepingHdr(), EncodeOn(Nvidia with { EncodesHdr = true }), JellyfinHost)
            .Should().Be($"-hwaccel cuda -hwaccel_output_format cuda -filter_threads 0 -filter_complex_threads 0 {Input} -c:v hevc_nvenc -preset p4 -tune hq -forced-idr 1 -rc vbr -cq 28 -qmin 28 -qmax 34 {Hdr10Out} {Keyframes} -vf scale_cuda=-2:1080:format=p010le -b:v 6800k -maxrate 8000k -bufsize 16000k" + HlsTail);
    }

    [Fact]
    public void Quick_sync_keeps_hdr10_scaling_in_10_bit_before_handing_frames_to_qsv()
    {
        Build(KeepingHdr(), EncodeOn(IntelQuickSync with { EncodesHdr = true }), JellyfinHost)
            .Should().Be($"-init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device qsv=qs@va -filter_hw_device va -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v hevc_qsv -preset medium -forced_idr 1 {Hdr10Out} {Keyframes} -vf scale_vaapi=w=-2:h=1080:format=p010,hwmap=derive_device=qsv,format=qsv -b:v 6800k -maxrate 8000k -rc_init_occupancy 13600k -bufsize 27200k" + HlsTail);
    }

    [Fact]
    public void Amd_keeps_hlg_with_its_own_transfer_and_skips_vulkan()
    {
        Build(KeepingHdr("HLG"), EncodeOn(AmdVaapiWithVulkan with { EncodesHdr = true }), JellyfinHost)
            .Should().Be($"-init_hw_device vaapi=va:/dev/dri/renderD128 -filter_hw_device va -hwaccel vaapi -hwaccel_output_format vaapi {Input} -c:v hevc_vaapi -profile:v main10 -color_primaries bt2020 -color_trc arib-std-b67 -colorspace bt2020nc -color_range tv {Keyframes} -vf scale_vaapi=w=-2:h=1080:format=p010 -rc_mode VBR -b:v 6800k -maxrate 8000k -bufsize 16000k" + HlsTail);
    }

    [Fact]
    public void A_gpu_that_failed_the_10_bit_test_tone_maps_to_h264_instead()
    {
        Build(KeepingHdr(), EncodeOn(Nvidia), JellyfinHost)
            .Should().Be($"-hwaccel cuda -hwaccel_output_format cuda -filter_threads 0 -filter_complex_threads 0 {Input} -c:v h264_nvenc -preset p4 -tune hq -forced-idr 1 {Keyframes} -vf scale_cuda=-2:1080:format=p010le,tonemap_cuda=tonemap=hable:format=nv12:peak=1000 -b:v 6800k -maxrate 8000k -bufsize 16000k" + HlsTail);
    }

    [Fact]
    public void A_transcode_on_the_cpu_tone_maps_to_h264_instead()
    {
        Build(KeepingHdr(), hardware: null, JellyfinHost)
            .Should().Be($"{Input} -c:v libx264 -preset veryfast -pix_fmt yuv420p {Keyframes} -vf scale=-2:1080,zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p -maxrate 8000k -bufsize 16000k" + HlsTail);
    }

    [Fact]
    public void Decoding_on_the_gpu_while_encoding_on_the_cpu_does_not_keep_hdr()
    {
        FFmpegTranscodeService.KeepsHdr(KeepingHdr(), DecodeOn(Nvidia with { EncodesHdr = true })).Should().BeFalse();
        FFmpegTranscodeService.KeepsHdr(KeepingHdr(), EncodeOn(Nvidia with { EncodesHdr = true })).Should().BeTrue();
    }

    [Fact]
    public void A_burned_in_subtitle_or_an_sdr_output_does_not_keep_hdr()
    {
        var burnIn = KeepingHdr();
        burnIn.RequiresSubtitleBurnIn = true;
        var sdr = KeepingHdr();
        sdr.OutputHdrType = "SDR";

        FFmpegTranscodeService.KeepsHdr(burnIn, EncodeOn(Nvidia with { EncodesHdr = true })).Should().BeFalse();
        FFmpegTranscodeService.KeepsHdr(sdr, EncodeOn(Nvidia with { EncodesHdr = true })).Should().BeFalse();
    }
}
