namespace Vora.Application.Settings.ViewModels;

public class FeatureFlagsVM
{
    public bool ForYou { get; set; }
    public bool ReleaseCalendar { get; set; }
    public bool Dvr { get; set; }
    public bool Podcasts { get; set; }

    // Switched on AND usable: the admin toggle plus a configured discovery
    // provider. Every row on the Discover page comes from one, and a provider
    // with no key contributes none, so an unconfigured server's Discover page
    // is blank.
    public bool Discover { get; set; }

    // Switched on AND usable: the admin toggle plus an IPTV source of that kind.
    // A client gates its navigation on these, and an entry that opens an empty
    // page is worse than no entry.
    public bool LiveTv { get; set; }
    public bool InternetRadio { get; set; }

    // What the admin actually stored, for the admin screens only. The switch has
    // to show the saved choice rather than the derived one, or a server with no
    // playlist shows it off and flipping it looks like it did nothing — and a
    // save that echoed a derived false back would erase the choice.
    public bool LiveTvEnabled { get; set; }
    public bool InternetRadioEnabled { get; set; }
    public bool DiscoverEnabled { get; set; }

    // Read-only: true when an installed subtitle-search provider is configured.
    // Not in UpdateFeatureFlagsRequest — it follows the plugin, so an admin sets
    // it by entering an API key rather than by flipping a switch.
    public bool SubtitleSearch { get; set; }

    // Read-only and derived: the admin's AI playlists toggle, For You, and an
    // OpenAI key. Requests additionally needs its own toggle. A profile can
    // still have switched AI playlists off for itself - see UserProfileVM.
    public bool AiPlaylists { get; set; }
    public bool AiPlaylistRequests { get; set; }
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
