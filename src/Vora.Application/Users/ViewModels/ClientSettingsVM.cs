using Vora.Application.Streaming.ViewModels;

namespace Vora.Application.Users.ViewModels;

public class ClientSettingsVM
{
    public required PlaybackPreferencesVM Playback { get; set; }
    public required TrackPreferencesVM Tracks { get; set; }
    public required LibrarySortVM LibrarySort { get; set; }
}
