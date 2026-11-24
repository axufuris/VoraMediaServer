using Vora.Application.Streaming.Requests;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Plugins;

namespace Vora.Application.Streaming;

public sealed record SubtitlePreference(SubtitleMode Mode, string? Language, bool PreferHearingImpaired);

public sealed record TrackPreferences(string? AudioLanguage, SubtitleMode? SubtitleMode, string? SubtitleLanguage, bool PreferHearingImpaired)
{
    public static readonly TrackPreferences Automatic = new(null, null, null, false);

    public static TrackPreferences From(SaveTrackPreferencesRequest request)
    {
        if (request.SubtitleMode is { } mode && !Enum.IsDefined(mode))
        {
            throw new ArgumentException($"'{mode}' is not a subtitle mode.", nameof(request));
        }

        return new TrackPreferences(
            LanguageCode(request.AudioLanguage),
            request.SubtitleMode,
            LanguageCode(request.SubtitleLanguage),
            request.PreferHearingImpairedSubtitles);
    }

    public TrackPreferencesVM ToViewModel(ServerSetting settings) => new()
    {
        AudioLanguage = AudioLanguage,
        SubtitleLanguage = SubtitleLanguage,
        SubtitleMode = SubtitleMode,
        PreferHearingImpairedSubtitles = PreferHearingImpaired,
        ServerLanguage = settings.MetadataLanguage,
        ServerSubtitleMode = ServerSubtitleMode(settings)
    };

    private static string? LanguageCode(string? language)
    {
        var code = MetadataLanguageCodes.Normalize(language);
        if (code is { Length: > 16 }) throw new ArgumentException("That is not a language code.", nameof(language));
        return code;
    }

    public static SubtitleMode ServerSubtitleMode(ServerSetting settings) =>
        settings.AutoEnableSubtitlesForForeignAudio ? Domain.Enums.SubtitleMode.Smart : Domain.Enums.SubtitleMode.ForcedOnly;

    public SubtitlePreference SubtitlesOn(ServerSetting settings) =>
        new(SubtitleMode ?? ServerSubtitleMode(settings), SubtitleLanguage ?? settings.MetadataLanguage, PreferHearingImpaired);
}
