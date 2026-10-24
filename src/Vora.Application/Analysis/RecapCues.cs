using System.Text.RegularExpressions;
using Vora.Application.Subtitles;

namespace Vora.Application.Analysis;

public static class RecapCues
{
    public static readonly TimeSpan SearchWindow = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex RecapLine = new(
        @"previously on|nell'episodio precedente|nelle puntate precedenti|negli episodi precedenti|^(was bisher geschah|bisher bei|zuvor bei|anteriormente en|anteriormente em|précédemment dans|wat voorafging|eerder in)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex Markup = new(@"<[^>]*>|\{[^}]*\}", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex LeadingLabels = new(@"^(\s*(-|–|♪|\[[^\]]*\]|\([^)]*\)|\p{Lu}[\p{Lu}\s.'’-]*:))+", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly HashSet<string> English = new(StringComparer.OrdinalIgnoreCase) { "en", "eng" };

    public static string Plain(string text) => Whitespace.Replace(Markup.Replace(text, " "), " ").Trim();

    public static string WithoutLabels(string plain) => LeadingLabels.Replace(plain, string.Empty).Trim();

    public static bool IsRecapLine(string text)
    {
        var plain = Plain(text);
        return RecapLine.IsMatch(plain) || RecapLine.IsMatch(WithoutLabels(plain));
    }

    public static TimeSpan? FirstRecapCueStart(IEnumerable<SubtitleCue> cues, TimeSpan startsBy) =>
        cues
            .Where(cue => cue.Start <= startsBy && IsRecapLine(cue.Text))
            .OrderBy(cue => cue.Start)
            .Select(cue => (TimeSpan?)cue.Start)
            .FirstOrDefault();

    public static SubtitleTrackTargetDto? PickTrack(IEnumerable<SubtitleTrackTargetDto> tracks) =>
        tracks
            .Where(track => !track.IsForced && SubtitlePreExtractionManager.IsExtractableSubtitleCodec(track.Codec))
            .OrderBy(track => IsEnglish(track.Language) ? 0 : 1)
            .ThenBy(track => track.ExternalFilePath is null ? 0 : 1)
            .ThenBy(track => track.StreamIndex)
            .FirstOrDefault();

    private static bool IsEnglish(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return false;
        var primary = language.Split('-', '_')[0];
        return English.Contains(primary);
    }
}
