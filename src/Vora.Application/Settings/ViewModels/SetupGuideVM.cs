using Vora.Domain.Enums;

namespace Vora.Application.Settings.ViewModels;

public class SetupGuideVM
{
    public const int MaxStepLength = 64;

    public SetupGuideStatus Status { get; set; }
    public string? Step { get; set; }
    public bool MoviesAndShows { get; set; }
    public bool Music { get; set; }
    public bool LiveTv { get; set; }
    public bool InternetRadio { get; set; }
    public bool Podcasts { get; set; }

    public static SetupGuideVM From(SetupGuideStatus status, string? step, SetupGuideContent content) => new()
    {
        Status = status,
        Step = step,
        MoviesAndShows = content.HasFlag(SetupGuideContent.MoviesAndShows),
        Music = content.HasFlag(SetupGuideContent.Music),
        LiveTv = content.HasFlag(SetupGuideContent.LiveTv),
        InternetRadio = content.HasFlag(SetupGuideContent.InternetRadio),
        Podcasts = content.HasFlag(SetupGuideContent.Podcasts)
    };

    public SetupGuideContent ToContent() =>
        (MoviesAndShows ? SetupGuideContent.MoviesAndShows : SetupGuideContent.None)
        | (Music ? SetupGuideContent.Music : SetupGuideContent.None)
        | (LiveTv ? SetupGuideContent.LiveTv : SetupGuideContent.None)
        | (InternetRadio ? SetupGuideContent.InternetRadio : SetupGuideContent.None)
        | (Podcasts ? SetupGuideContent.Podcasts : SetupGuideContent.None);
}
