using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public readonly record struct VideoRate(int TargetKbps, int MaxKbps);

public static class VideoBitrate
{
    public const double Headroom = 0.85;
    public const int MaxSaneKbps = 400_000;
    private const double PixelExponent = 0.75;

    public static double CodecEfficiency(VideoCodec codec) => codec switch
    {
        VideoCodec.Hevc or VideoCodec.Vp9 => 0.6,
        VideoCodec.Av1 => 0.5,
        _ => 1.0,
    };

    public static double CodecEfficiency(string? codec) => codec?.Trim().ToLowerInvariant() switch
    {
        "hevc" or "h265" or "vp9" => 0.6,
        "av1" => 0.5,
        _ => 1.0,
    };

    public static int TargetFromCap(int maxKbps) => (int)Math.Round(maxKbps * Headroom);

    public static VideoRate For(
        int sourceKbps,
        string? sourceCodec,
        int? sourceBitDepth,
        FrameSize? source,
        FrameSize? output,
        VideoCodec target,
        int ceilingKbps,
        int profileCapKbps)
    {
        double kbps = Math.Max(1, sourceKbps);

        if (source is { Pixels: > 0 } from && output is { Pixels: > 0 } to && to.Pixels < from.Pixels)
        {
            kbps *= Math.Pow((double)to.Pixels / from.Pixels, PixelExponent);
        }

        if (sourceBitDepth > 8 && target == VideoCodec.H264)
        {
            kbps *= sourceKbps switch
            {
                <= 4_000 => 1.5,
                <= 8_000 => 1.2,
                <= 12_000 => 1.1,
                _ => 1.05,
            };
        }

        kbps *= CodecFactor(kbps, sourceCodec, target);

        var targetKbps = Math.Min(kbps, MaxSaneKbps);
        if (profileCapKbps > 0) targetKbps = Math.Min(targetKbps, profileCapKbps);
        if (ceilingKbps > 0) targetKbps = Math.Min(targetKbps, ceilingKbps * Headroom);

        var average = Math.Max(1, (int)Math.Round(targetKbps));
        var peak = (int)Math.Ceiling(average / Headroom);
        if (ceilingKbps > 0) peak = Math.Min(peak, ceilingKbps);
        return new VideoRate(average, Math.Max(average, peak));
    }

    private static double CodecFactor(double kbps, string? sourceCodec, VideoCodec target)
    {
        var factor = Math.Max(CodecEfficiency(target) / CodecEfficiency(sourceCodec), 1);
        if (kbps <= 500) return Math.Max(factor, 4);
        if (kbps <= 1_000) return Math.Max(factor, 3);
        if (kbps <= 2_000) return Math.Max(factor, 2.5);
        if (kbps <= 3_000) return Math.Max(factor, 2);
        if (kbps >= 30_000) return 1;
        return factor;
    }
}
