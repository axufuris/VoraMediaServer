using Vora.Application.Metadata;
using Vora.Application.Streaming;

namespace Vora.Application.Media;

public sealed class ExtraThumbnailPartDto
{
    public Guid Id { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
}

public sealed class ExtraThumbnailSourceDto
{
    public Guid ExtraId { get; init; }
    public string? FallbackUrl { get; init; }
    public ExtraThumbnailPartDto? Part { get; init; }
}

public sealed record ExtraThumbnail(string? FramePath, string? FallbackUrl);

public interface IExtraThumbnailService
{
    Task<ExtraThumbnail?> GetAsync(Guid extraId, CancellationToken cancellationToken = default);
}

public sealed class ExtraThumbnailService(IStreamRepository streams, IVideoFrameArtworkService frames) : IExtraThumbnailService
{
    public async Task<ExtraThumbnail?> GetAsync(Guid extraId, CancellationToken cancellationToken = default)
    {
        var source = await streams.GetExtraThumbnailSourceAsync(extraId);
        if (source == null) return null;

        var framePath = source.Part == null
            ? null
            : await frames.GetExtraThumbnailAsync(source.ExtraId, source.Part.Id, source.Part.FilePath, source.Part.Duration, cancellationToken);
        var fallbackUrl = string.IsNullOrWhiteSpace(source.FallbackUrl) ? null : source.FallbackUrl;

        return framePath == null && fallbackUrl == null ? null : new ExtraThumbnail(framePath, fallbackUrl);
    }
}
