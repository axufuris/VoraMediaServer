using Vora.Application.Streaming.ViewModels;

namespace Vora.Application.Streaming;

public interface ITranscodeService
{
    Task<string> StartTranscodeSessionAsync(string sourceFilePath, string outputDirectory, PlaybackDecisionVM decision, CancellationToken cancellationToken = default);
    int GetActiveTranscodeCount();
    bool IsMediaTranscoding(Guid mediaItemId);
}