namespace Vora.Application.Streaming;

public interface ISubtitleExtractionService
{
    Task<string?> ExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string outputDirectory, Guid transcodeKey, CancellationToken cancellationToken = default);
    void RemoveWebVtt(string outputDirectory, Guid transcodeKey);
}
