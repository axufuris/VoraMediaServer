using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

/// <summary>The playback transcodes <see cref="FFmpegTranscodeService"/> is running.</summary>
public sealed class PlaybackEncodes : IEncodeSource
{
    public IReadOnlyList<RunningEncode> RunningEncodes(Guid except) => FFmpegTranscodeService.RunningEncodes(except);
}
