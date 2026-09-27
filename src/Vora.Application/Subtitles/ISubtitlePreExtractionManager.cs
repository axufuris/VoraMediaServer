namespace Vora.Application.Subtitles;

public interface ISubtitlePreExtractionManager
{
    Task PreExtractForItemAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
    Task PreExtractForLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task BackfillAsync(CancellationToken cancellationToken = default);
    Task PurgeItemAsync(Guid mediaItemId);
    void PurgePart(Guid mediaPartId, string transcodeTempDirectory);
    Task<string> ResolveCacheDirectoryRootAsync();
}
