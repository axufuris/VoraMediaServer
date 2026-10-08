namespace Vora.Application.Streaming;

public interface IAudioTranscodeService
{
    string ResolveContentType(string targetCodec);

    Task<string?> GetTranscodedFileAsync(Guid trackId, string sourceFilePath, int bitrateKbps, string targetCodec, string transcodeTempDirectory, CancellationToken cancellationToken);
}
