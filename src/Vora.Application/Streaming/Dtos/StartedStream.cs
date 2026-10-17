using Vora.Domain.Entities.Streaming;

namespace Vora.Application.Streaming.Dtos;

public sealed record StartedStream(StreamSession Session, string StreamUrl, int AudioTrackPosition, IReadOnlyList<string> Reasons);
