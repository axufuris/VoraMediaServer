namespace Vora.Application.Media.ViewModels;

public class MoodSummaryVM
{
    public string Mood { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public string? SampleArtworkUrl { get; set; }
}

public class MoodTracksVM
{
    public string Mood { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public List<ArtistTrackVM> Tracks { get; set; } = new();
}
