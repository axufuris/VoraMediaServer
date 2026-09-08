namespace Vora.Application.Streaming;

public interface ISubtitleExtractionService
{
    bool HasValidCachedWebVtt(string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, SubtitleSource source);
    Task<string?> GetOrExtractWebVttAsync(SubtitleSource source, string transcodeTempDirectory, Guid mediaPartId, Guid subtitleTrackId, CancellationToken cancellationToken = default);
    void PurgePart(string transcodeTempDirectory, Guid mediaPartId);
    IReadOnlyCollection<Guid> ListCachedPartIds(string transcodeTempDirectory);
}
