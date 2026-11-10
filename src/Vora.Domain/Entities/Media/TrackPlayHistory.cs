using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Media;

public class TrackPlayHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Guid TrackId { get; set; }
    public virtual Track Track { get; set; } = null!;
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public int DurationListenedSeconds { get; set; }
    public bool Completed { get; set; }

    // The playlist, smart playlist, mix or album the song was played from, when
    // the client said; None for anything else and for plays recorded before.
    public MusicPlaySource Source { get; set; }
    public Guid? SourceId { get; set; }
}
