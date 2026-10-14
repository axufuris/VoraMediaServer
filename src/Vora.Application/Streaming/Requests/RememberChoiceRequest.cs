using Vora.Domain.Enums;

namespace Vora.Application.Streaming.Requests;

public sealed class RememberChoiceRequest
{
    public Guid? MediaPartId { get; set; }
    public Guid? AudioTrackId { get; set; }
    public Guid? SubtitleTrackId { get; set; }
    public bool SubtitlesOff { get; set; }
    public List<PlaybackChoiceKind> Forget { get; set; } = [];
}
