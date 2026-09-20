namespace Vora.Application.Media.Requests;

public class UpdateArtistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? SortName { get; set; }
    public string? Biography { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ClearLogoUrl { get; set; }
    public List<string> LockedFields { get; set; } = new();
}
