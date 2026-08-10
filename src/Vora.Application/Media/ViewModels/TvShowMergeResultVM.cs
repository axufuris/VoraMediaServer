namespace Vora.Application.Media.ViewModels;

public class TvShowMergeResultVM
{
    public int GroupsMerged { get; set; }
    public int ShowsRemoved { get; set; }
    public int PartsMoved { get; set; }
    public List<Guid> AffectedEpisodeIds { get; set; } = new();
}
