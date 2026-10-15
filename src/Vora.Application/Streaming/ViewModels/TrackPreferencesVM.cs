using Vora.Domain.Enums;

namespace Vora.Application.Streaming.ViewModels;

public class TrackPreferencesVM
{
    public string? AudioLanguage { get; set; }
    public string? SubtitleLanguage { get; set; }
    public SubtitleMode? SubtitleMode { get; set; }
    public bool PreferHearingImpairedSubtitles { get; set; }
    public required string ServerLanguage { get; set; }
    public required SubtitleMode ServerSubtitleMode { get; set; }
}
