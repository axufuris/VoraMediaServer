using Vora.Domain.Enums;

namespace Vora.Application.Media.ViewModels;

public class MusicResumeItemVM : RecentlyPlayedItemVM
{
    public MusicPlaySource Source { get; set; }
    public Guid? SourceId { get; set; }
    public int TrackIndex { get; set; }
}

public class MusicResumeQueueVM
{
    public MusicPlaySource Source { get; set; }
    public Guid? SourceId { get; set; }
    public int Index { get; set; }
    public List<ArtistTrackVM> Tracks { get; set; } = new();
}
