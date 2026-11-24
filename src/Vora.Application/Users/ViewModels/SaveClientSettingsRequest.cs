using Vora.Application.Streaming.Requests;

namespace Vora.Application.Users.ViewModels;

public class SaveClientSettingsRequest
{
    public PlaybackPreferencesVM? Playback { get; set; }
    public SaveTrackPreferencesRequest? Tracks { get; set; }
    public LibrarySortVM? LibrarySort { get; set; }
}
