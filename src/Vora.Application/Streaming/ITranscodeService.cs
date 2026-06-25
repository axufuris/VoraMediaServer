using Vora.Application.Streaming.ViewModels;

namespace Vora.Application.Streaming;

public interface ITranscodeService
{
    Task<string> StartTranscodeSessionAsync(string sourceFilePath, string outputDirectory, PlaybackDecisionVM decision, CancellationToken cancellationToken = default);
    Task StopTranscodeSessionAsync(Guid mediaItemId);
    Task<bool> EnsureSegmentAvailableAsync(Guid mediaItemId, int segmentIndex, CancellationToken cancellationToken = default);
    void NotifySegmentServed(Guid mediaItemId, int segmentIndex);
    void TouchSession(Guid mediaItemId);
    Task EvictIdleSessionsAsync(TimeSpan maxIdle);
    Task ReapOrphanedTranscodeFilesAsync(string targetDir, TimeSpan maxAge);
    int GetActiveTranscodeCount();
    bool IsMediaTranscoding(Guid mediaItemId);
}