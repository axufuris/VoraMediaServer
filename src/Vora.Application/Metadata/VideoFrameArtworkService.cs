using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vora.Application.Settings;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Metadata;

public interface IVideoFrameArtworkService
{
    Task<List<MediaArtwork>> GetFrameArtworkAsync(MediaItem item, bool needsPoster, bool needsBackdrop, CancellationToken cancellationToken = default);
}

public sealed class VideoFrameArtworkService(
    IVideoThumbnailGeneratorService generator,
    IOptions<StoragePathsOptions> storagePaths,
    ILogger<VideoFrameArtworkService> logger) : IVideoFrameArtworkService
{
    public const string ProviderId = "video_frame";
    public static readonly Size PosterSize = new(720, 1080);
    public static readonly Size BackdropSize = new(1920, 1080);

    private const string UrlPrefix = "/api/artwork/custom/";
    private const double DarkFrameLuminance = 0.08;
    private static readonly double[] FramePositions = [1d / 3, 1d / 2, 2d / 3];

    private readonly string _artworkFolder = StorageRoots.CustomArtwork(storagePaths.Value);

    public async Task<List<MediaArtwork>> GetFrameArtworkAsync(MediaItem item, bool needsPoster, bool needsBackdrop, CancellationToken cancellationToken = default)
    {
        if (!needsPoster && !needsBackdrop) return [];

        var part = item.MediaParts.OrderBy(p => p.Id).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.FilePath) && File.Exists(p.FilePath));
        if (part == null) return [];

        var poster = Path.Combine(_artworkFolder, $"media_{item.Id:N}_{part.Id:N}_frame_poster.jpg");
        var backdrop = Path.Combine(_artworkFolder, $"media_{item.Id:N}_{part.Id:N}_frame_backdrop.jpg");
        var missing = (needsPoster && !File.Exists(poster)) || (needsBackdrop && !File.Exists(backdrop));
        if (missing && !await WriteFrameArtworkAsync(part, poster, backdrop, cancellationToken)) return [];

        var artwork = new List<MediaArtwork>();
        if (needsPoster) artwork.Add(Artwork(item, ArtworkKind.Poster, poster, PosterSize));
        if (needsBackdrop) artwork.Add(Artwork(item, ArtworkKind.Backdrop, backdrop, BackdropSize));
        return artwork;
    }

    public static Image<Rgb24> Compose(Image<Rgb24> frame, Size size)
    {
        var canvas = frame.Clone(x => x
            .Resize(new ResizeOptions { Size = new Size(Math.Max(1, size.Width / 8), Math.Max(1, size.Height / 8)), Mode = ResizeMode.Crop })
            .GaussianBlur(4)
            .Resize(size)
            .Brightness(0.55f));
        using var picture = frame.Clone(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Max }));
        canvas.Mutate(x => x.DrawImage(picture, new Point((size.Width - picture.Width) / 2, (size.Height - picture.Height) / 2), 1f));
        return canvas;
    }

    public static double MeanLuminance(Image<Rgb24> frame)
    {
        using var small = frame.Clone(x => x.Resize(16, 16));
        double total = 0;
        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                var pixel = small[x, y];
                total += (0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B) / 255;
            }
        }
        return total / (small.Width * small.Height);
    }

    private async Task<bool> WriteFrameArtworkAsync(MediaPart part, string posterPath, string backdropPath, CancellationToken cancellationToken)
    {
        using var frame = await GrabFrameAsync(part, cancellationToken);
        if (frame == null)
        {
            logger.LogWarning("Could not take a frame from {FilePath} for its poster", part.FilePath);
            return false;
        }

        Directory.CreateDirectory(_artworkFolder);
        using (var poster = Compose(frame, PosterSize)) await SaveAsync(poster, posterPath, cancellationToken);
        using (var backdrop = Compose(frame, BackdropSize)) await SaveAsync(backdrop, backdropPath, cancellationToken);
        return true;
    }

    private async Task<Image<Rgb24>?> GrabFrameAsync(MediaPart part, CancellationToken cancellationToken)
    {
        var duration = part.Duration ?? await generator.ProbeDurationAsync(part.FilePath, cancellationToken);
        IEnumerable<TimeSpan> positions = duration is { TotalSeconds: > 1 } known
            ? FramePositions.Select(position => known * position)
            : [TimeSpan.Zero];

        Image<Rgb24>? brightest = null;
        var brightestLuminance = -1d;
        foreach (var at in positions)
        {
            var temp = Path.Combine(Path.GetTempPath(), $"vora-frame-{Guid.NewGuid():N}.jpg");
            try
            {
                if (!await generator.ExtractFrameAsync(part.FilePath, at, temp, cancellationToken)) continue;

                var frame = await Image.LoadAsync<Rgb24>(temp, cancellationToken);
                var luminance = MeanLuminance(frame);
                if (luminance >= DarkFrameLuminance)
                {
                    brightest?.Dispose();
                    return frame;
                }

                if (luminance > brightestLuminance)
                {
                    brightest?.Dispose();
                    brightest = frame;
                    brightestLuminance = luminance;
                }
                else
                {
                    frame.Dispose();
                }
            }
            catch (Exception ex) when (ex is IOException or ImageFormatException)
            {
                logger.LogWarning(ex, "Could not read the frame taken from {FilePath} at {At}", part.FilePath, at);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        return brightest;
    }

    private static async Task SaveAsync(Image<Rgb24> image, string path, CancellationToken cancellationToken)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await image.SaveAsJpegAsync(temp, new JpegEncoder { Quality = 85 }, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    private static MediaArtwork Artwork(MediaItem item, ArtworkKind kind, string path, Size size) => new()
    {
        Url = UrlPrefix + Path.GetFileName(path),
        Kind = kind,
        Width = size.Width,
        Height = size.Height,
        ProviderId = ProviderId,
        MediaItemId = item.Id
    };
}
