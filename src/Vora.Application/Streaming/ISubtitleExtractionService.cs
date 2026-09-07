namespace Vora.Application.Streaming;

public interface ISubtitleExtractionService
{
    bool TryGetCachedWebVtt(string cacheDirectory, Guid mediaPartId, Guid subtitleTrackId, out string fileName);
    Task BeginExtractionAsync(string sourceFilePath, int subtitleStreamIndex, int subtitleOrdinal, string cacheDirectory, Guid mediaPartId, Guid subtitleTrackId);
}
