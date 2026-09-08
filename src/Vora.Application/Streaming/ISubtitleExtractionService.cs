namespace Vora.Application.Streaming;

public interface ISubtitleExtractionService
{
    bool HasValidCachedWebVtt(string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, string sourceFilePath, int subtitleStreamIndex);
    Task<string?> GetOrExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default);
    void PurgePart(string transcodeTempDirectory, Guid mediaPartId);
    IReadOnlyCollection<Guid> ListCachedPartIds(string transcodeTempDirectory);
}
