namespace Vora.Application.Streaming;

public interface ISubtitleExtractionService
{
    Task<string?> GetOrExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default);
}
