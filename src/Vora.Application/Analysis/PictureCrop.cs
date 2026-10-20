using Vora.Domain.Entities.Media;

namespace Vora.Application.Analysis;

public readonly record struct CropBox(int X, int Y, int Width, int Height);

public sealed record PictureCrop(int Top, int Right, int Bottom, int Left)
{
    public const int MinimumSamples = 3;
    private const double SmallestPicture = 0.25;
    private const double EdgeTolerance = 0.01;
    private const int EdgeTolerancePixels = 4;

    public static PictureCrop None { get; } = new(0, 0, 0, 0);

    public bool IsNone => Top == 0 && Right == 0 && Bottom == 0 && Left == 0;

    public static PictureCrop FromBoxes(int frameWidth, int frameHeight, IEnumerable<CropBox> boxes)
    {
        var pictures = boxes.Where(box => Fits(box, frameWidth, frameHeight)).ToList();
        if (pictures.Count < MinimumSamples) return None;

        var top = pictures.Min(box => box.Y);
        var left = pictures.Min(box => box.X);
        var right = frameWidth - pictures.Max(box => box.X + box.Width);
        var bottom = frameHeight - pictures.Max(box => box.Y + box.Height);
        return new PictureCrop(Edge(top, frameHeight), Edge(right, frameWidth), Edge(bottom, frameHeight), Edge(left, frameWidth));
    }

    public static PictureCrop? Of(MediaVideoTrack track) =>
        track.CropTop is int top && track.CropRight is int right && track.CropBottom is int bottom && track.CropLeft is int left
            ? new PictureCrop(top, right, bottom, left)
            : null;

    public void ApplyTo(MediaVideoTrack track)
    {
        track.CropTop = Top;
        track.CropRight = Right;
        track.CropBottom = Bottom;
        track.CropLeft = Left;
    }

    public static void Forget(MediaVideoTrack track)
    {
        track.CropTop = null;
        track.CropRight = null;
        track.CropBottom = null;
        track.CropLeft = null;
    }

    public static bool StreamChanged(MediaVideoTrack existing, MediaVideoTrack incoming) =>
        existing.Width != incoming.Width
        || existing.Height != incoming.Height
        || existing.Codec != incoming.Codec
        || existing.Bitrate != incoming.Bitrate;

    private static bool Fits(CropBox box, int frameWidth, int frameHeight) =>
        box.X >= 0 && box.Y >= 0
        && box.Width >= frameWidth * SmallestPicture && box.Height >= frameHeight * SmallestPicture
        && box.X + box.Width <= frameWidth && box.Y + box.Height <= frameHeight;

    private static int Edge(int inset, int dimension) =>
        inset <= Math.Max(EdgeTolerancePixels, dimension * EdgeTolerance) ? 0 : inset;
}
