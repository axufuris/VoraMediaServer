namespace Vora.Application.Streaming;

public interface IAudioTranscodeService
{
    string ResolveContentType(string targetCodec);

    Task WriteTranscodedAudioAsync(string sourceFilePath, int bitrateKbps, string targetCodec, Stream output, CancellationToken cancellationToken);
}
