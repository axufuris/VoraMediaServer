namespace Vora.Application.YouTube.ViewModels;

public class YouTubeStatusVM
{
    public bool PluginInstalled { get; set; }
    public bool ApiKeyConfigured { get; set; }
    public bool ServerEnabled { get; set; }
    public string TrendingRegion { get; set; } = "US";
}
