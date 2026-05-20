using Vora.Application.Streaming.ViewModels;

namespace Vora.Application.Streaming;

public interface ITranscodeService
{
    Task<string> StartTranscodeSessionAsync(string sourceFilePath, string outputDirectory, PlaybackDecisionVM decision);
    Task StopTranscodeSessionAsync(Guid mediaItemId);
    int GetActiveTranscodeCount();
    bool IsMediaTranscoding(Guid mediaItemId);
}