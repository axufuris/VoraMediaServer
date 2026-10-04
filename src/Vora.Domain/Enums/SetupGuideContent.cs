namespace Vora.Domain.Enums;

[Flags]
public enum SetupGuideContent
{
    None = 0,
    MoviesAndShows = 1,
    Music = 2,
    LiveTv = 4,
    InternetRadio = 8,
    Podcasts = 16
}
