using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public readonly record struct FrameSize(int Width, int Height)
{
    public long Pixels => (long)Width * Height;
}

public static class OutputSize
{
    public const int DefaultMaxHeight = 2160;
    public const int MaxEncoderDimension = 4096;
    public static readonly FrameSize HdrDownscaleBox = new(1920, 1080);

    private static readonly (int Width, int Kbps)[] WidthForBitrateLadder =
    [
        (416, 365),
        (640, 730),
        (768, 1100),
        (960, 3000),
        (1280, 6000),
        (1920, 13500),
        (2560, 28000),
        (3840, 50000),
    ];

    public static FrameSize? Source(int? width, int? height, string? resolutionLabel)
    {
        if (width is > 0 && height is > 0) return new FrameSize(width.Value, height.Value);
        if (string.IsNullOrWhiteSpace(resolutionLabel)) return null;
        var labelHeight = BestPathDecisionManager.ParseHeightFromResolution(resolutionLabel);
        return labelHeight > 0 ? new FrameSize(Widescreen(labelHeight), labelHeight) : null;
    }

    public static FrameSize ClientBox(int requestedMaxWidth, int requestedMaxHeight)
    {
        var height = requestedMaxHeight > 0 ? requestedMaxHeight : DefaultMaxHeight;
        var width = requestedMaxWidth > 0 ? requestedMaxWidth : Widescreen(height);
        return new FrameSize(width, height);
    }

    public static FrameSize Within(FrameSize box, FrameSize other) =>
        new(Math.Min(box.Width, other.Width), Math.Min(box.Height, other.Height));

    public static bool Exceeds(FrameSize source, FrameSize box) => source.Width > box.Width || source.Height > box.Height;

    public static FrameSize Fit(FrameSize source, FrameSize box)
    {
        var maxWidth = Math.Min(box.Width, MaxEncoderDimension);
        var maxHeight = Math.Min(box.Height, MaxEncoderDimension);
        if (source.Width <= maxWidth && source.Height <= maxHeight) return source;

        var scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        var width = Math.Min(maxWidth, (int)Math.Round(source.Width * scale));
        var height = Math.Min(maxHeight, (int)Math.Round(source.Height * scale));
        return new FrameSize(Math.Max(2, width / 2 * 2), Math.Max(2, height / 2 * 2));
    }

    public static int HeightClass(FrameSize size)
    {
        var longSide = Math.Max(size.Width, size.Height);
        var shortSide = Math.Min(size.Width, size.Height);
        return Math.Max(shortSide, (int)Math.Round(longSide * 9 / 16.0));
    }

    public static int MaxWidthForBitrate(int kbps, double? frameRate, VideoCodec target)
    {
        if (kbps <= 0) return int.MaxValue;
        var h264Equivalent = kbps / VideoBitrate.CodecEfficiency(target);
        var fps = frameRate is > 0 ? frameRate.Value : 30.0;
        var perFrame = fps <= 30 ? 30 / fps : 1 / Math.Sqrt(fps / 30);
        var reference = h264Equivalent * perFrame;
        foreach (var (width, ladderKbps) in WidthForBitrateLadder)
        {
            if (reference <= ladderKbps) return width;
        }
        return int.MaxValue;
    }

    private static int Widescreen(int height) => (int)Math.Round(height * 16 / 9.0);
}
