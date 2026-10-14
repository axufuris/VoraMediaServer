using Vora.Application.Settings;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

internal static class TranscodeArgumentScenarios
{
    public const string Source = "/media/movies/Heat (1995)/Heat.mkv";
    public const string Segments = "/transcode/heat_%d.ts";
    public const string InternalPlaylist = "/transcode/_ffmpeg_heat.m3u8";
    public const string RenderNode = "/dev/dri/renderD128";

    public static readonly FFmpegTranscodeService.TranscodeHost JellyfinHost = new(HasJellyfinFfmpeg: true, CanUseGpuHdrTonemap: true);
    public static readonly FFmpegTranscodeService.TranscodeHost PlainHost = new(HasJellyfinFfmpeg: false, CanUseGpuHdrTonemap: false);

    public static readonly HardwareAccelerator Nvidia = new(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia);
    public static readonly HardwareAccelerator IntelQuickSync = new(HardwareAccelerationType.QuickSync, RenderNode, GpuVendor.Intel);
    public static readonly HardwareAccelerator IntelVaapi = new(HardwareAccelerationType.Vaapi, RenderNode, GpuVendor.Intel);
    public static readonly HardwareAccelerator AmdVaapi = new(HardwareAccelerationType.Vaapi, RenderNode, GpuVendor.Amd);
    public static readonly HardwareAccelerator AmdVaapiWithVulkan = AmdVaapi with { HasVulkanInterop = true };

    public static FFmpegTranscodeService.TranscodeHardware EncodeOn(HardwareAccelerator gpu) => new(gpu, Encode: true);
    public static FFmpegTranscodeService.TranscodeHardware DecodeOn(HardwareAccelerator gpu) => new(gpu, Encode: false);

    public static PlaybackDecisionVM Transcode(VideoCodec target = VideoCodec.H264, string hdr = "SDR", int outputHeight = 0, int bandwidthKbps = 0, bool burnIn = false, string? externalSubtitle = null) => new()
    {
        MediaItemId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Decision = StreamingState.Transcode,
        VideoStrategy = "Transcode",
        AudioStrategy = "Copy",
        SourceAudioCodec = "aac",
        TargetVideoCodec = target,
        TargetAudioCodec = AudioCodec.Aac,
        SourceHdrType = hdr,
        OutputHeight = outputHeight,
        BandwidthKbps = bandwidthKbps,
        RequiresSubtitleBurnIn = burnIn,
        SelectedVideoStreamIndex = 0,
        SelectedAudioStreamIndex = 1,
        SelectedSubtitleStreamIndex = burnIn ? 3 : null,
        SelectedSubtitleExternalPath = externalSubtitle,
    };

    public static PlaybackDecisionVM DirectStream() => new()
    {
        MediaItemId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Decision = StreamingState.Remux,
        VideoStrategy = "Copy",
        AudioStrategy = "Transcode",
        SourceAudioCodec = "truehd",
        TargetAudioCodec = AudioCodec.Ac3,
        TargetAudioChannels = 6,
        SelectedVideoStreamIndex = 0,
        SelectedAudioStreamIndex = 2,
    };

    public static string Build(PlaybackDecisionVM decision, FFmpegTranscodeService.TranscodeHardware? hardware, FFmpegTranscodeService.TranscodeHost host, int startSegment = 0) =>
        string.Join(" ", FFmpegTranscodeService.BuildFFmpegArguments(Source, Segments, InternalPlaylist, decision, new ServerSetting(), hardware, SegmentLaunch.Uniform(startSegment), host));
}
