using Vora.Domain.Enums;

namespace Vora.Application.Streaming.Requests;

public class SaveTrackPreferencesRequest
{
    public string? AudioLanguage { get; set; }
    public string? SubtitleLanguage { get; set; }
    public SubtitleMode? SubtitleMode { get; set; }
    public bool PreferHearingImpairedSubtitles { get; set; }
}
