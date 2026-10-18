using Vora.Domain.Entities.Streaming;

namespace Vora.Application.Streaming.Dtos;

public sealed record StartedStream(StreamSession Session, string StreamUrl, int AudioTrackPosition, int? SubtitleTrackPosition, IReadOnlyList<string> Reasons);
