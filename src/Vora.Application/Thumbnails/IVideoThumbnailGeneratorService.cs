namespace Vora.Application.Thumbnails;

public class VideoThumbnailGenerationParameters
{
    public required string InputPath { get; init; }
    public int IntervalSeconds { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int JpegQuality { get; init; }
    public int SpriteColumns { get; init; }
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
}
