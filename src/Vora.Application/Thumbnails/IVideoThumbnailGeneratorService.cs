namespace Vora.Application.Thumbnails;

public class VideoThumbnailGenerationParameters
{
    public required string InputPath { get; init; }
    public int IntervalSeconds { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int JpegQuality { get; init; }
    public int SpriteColumns { get; init; }

    // Decode on the GPU (NVDEC via -hwaccel) when hardware acceleration is on;
    // the sprite extraction decodes the whole file, so 10-bit HEVC benefits a lot.
    // Falls back to software if the hardware pass fails.
    public bool UseHardwareDecode { get; init; }
    public string? HardwareDevice { get; init; }
}

public class VideoThumbnailGenerationResult
{
    public required string SpriteOutputPath { get; init; }
    public required string VttOutputPath { get; init; }
    public int SpriteCount { get; init; }
    public int SpriteColumns { get; init; }
    public int SpriteRows { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int IntervalSeconds { get; init; }
    public TimeSpan SourceDuration { get; init; }
}

public interface IVideoThumbnailGeneratorService
{
    Task<TimeSpan?> ProbeDurationAsync(string inputPath, CancellationToken cancellationToken = default);

    Task<VideoThumbnailGenerationResult> GenerateAsync(
        VideoThumbnailGenerationParameters parameters,
        string finalSpritePath,
        string finalVttPath,
        CancellationToken cancellationToken = default);

    // Grabs a single full-frame JPEG from the video at the given offset (used as
    // a fallback still for episodes that have no artwork). Returns true on success.
    Task<bool> ExtractFrameAsync(string inputPath, TimeSpan at, string outputPath, CancellationToken cancellationToken = default);
}
