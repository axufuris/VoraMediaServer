namespace Vora.Application.Analysis;

public sealed record PictureAreaTarget(Guid VideoTrackId, Guid MediaItemId, string FilePath, int StreamIndex, int Width, int Height, double DurationSeconds);

public interface IPictureAreaDetector
{
    Task<PictureCrop?> DetectAsync(PictureAreaTarget target, CancellationToken cancellationToken = default);
}
