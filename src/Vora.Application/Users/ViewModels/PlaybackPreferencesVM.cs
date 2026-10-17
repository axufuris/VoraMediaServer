namespace Vora.Application.Users.ViewModels;

public class PlaybackPreferencesVM
{
    public bool AutoSkipIntro { get; set; }
    public bool AutoSkipCredits { get; set; }
    public bool? AutoPlayNext { get; set; }
    public int MinimumCreditsSceneSeconds { get; set; } = 15;
}
