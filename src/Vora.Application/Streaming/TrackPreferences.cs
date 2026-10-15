using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public sealed record SubtitlePreference(SubtitleMode Mode, string? Language, bool PreferHearingImpaired);

public sealed record TrackPreferences(string? AudioLanguage, SubtitleMode? SubtitleMode, string? SubtitleLanguage, bool PreferHearingImpaired)
{
    public static readonly TrackPreferences Automatic = new(null, null, null, false);

    public static SubtitleMode ServerSubtitleMode(ServerSetting settings) =>
        settings.AutoEnableSubtitlesForForeignAudio ? Domain.Enums.SubtitleMode.Smart : Domain.Enums.SubtitleMode.Default;

    public SubtitlePreference SubtitlesOn(ServerSetting settings) =>
        new(SubtitleMode ?? ServerSubtitleMode(settings), SubtitleLanguage ?? settings.MetadataLanguage, PreferHearingImpaired);
}
