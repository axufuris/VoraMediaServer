namespace Vora.Application.Settings.ViewModels;

public class FeatureFlagsVM
{
    public bool Discover { get; set; }
    public bool ForYou { get; set; }
    public bool ReleaseCalendar { get; set; }
    public bool LiveTv { get; set; }
    public bool Dvr { get; set; }
    public bool InternetRadio { get; set; }
    public bool Podcasts { get; set; }

    // Read-only: true when an installed subtitle-search provider is configured.
    // Not in UpdateFeatureFlagsRequest — it follows the plugin, so an admin sets
    // it by entering an API key rather than by flipping a switch.
    public bool SubtitleSearch { get; set; }
}

public class UpdateFeatureFlagsRequest
{
    public bool Discover { get; set; } = true;
    public bool ForYou { get; set; } = true;
    public bool ReleaseCalendar { get; set; } = true;
    public bool LiveTv { get; set; } = true;
    public bool Dvr { get; set; } = true;
    public bool InternetRadio { get; set; } = true;
    public bool Podcasts { get; set; } = true;
}
