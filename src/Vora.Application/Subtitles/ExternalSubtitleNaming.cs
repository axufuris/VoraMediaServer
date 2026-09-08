using System.Text.RegularExpressions;

namespace Vora.Application.Subtitles;

public sealed record ExternalSubtitleFile(
    string FilePath,
    string Codec,
    string? Language,
    string? Title,
    bool IsForced,
    bool IsSdh);

// Sidecar subtitles are named after the video with dot-separated hints:
//
//   Movie (2026).srt
//   Movie (2026).en.srt
//   Movie (2026).en.forced.srt
//   Movie (2026).eng.sdh.ass
//
// Everything about the track — language, forced, hearing-impaired — is carried
// in those segments, so parsing them is the whole of what makes a sidecar a
// usable track rather than an anonymous file.
public static class ExternalSubtitleNaming
{
    // .sub is the VobSub bitmap format (paired with a .idx). It is included so
    // the track is selectable, but it can only ever be burned in — there is no
    // text to convert to WebVTT.
    private static readonly Dictionary<string, string> CodecByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".srt"] = "subrip",
        [".ass"] = "ass",
        [".ssa"] = "ssa",
        [".vtt"] = "webvtt",
        [".sub"] = "vobsub",
    };

    private static readonly HashSet<string> ForcedMarkers = new(StringComparer.OrdinalIgnoreCase) { "forced" };

    private static readonly HashSet<string> SdhMarkers = new(StringComparer.OrdinalIgnoreCase) { "sdh", "cc", "hi" };

    // Two or three letters, optionally with a region ("pt-BR", "zh_Hans").
    private static readonly Regex LanguageSegment = new(@"^[a-z]{2,3}([-_][a-z0-9]{2,4})?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSubtitleExtension(string fileName) =>
        CodecByExtension.ContainsKey(Path.GetExtension(fileName));

    public static string? CodecForExtension(string fileName) =>
        CodecByExtension.TryGetValue(Path.GetExtension(fileName), out var codec) ? codec : null;

    // Matches only a sidecar for THIS video. The separator must be a literal dot,
    // so "Movie (2026) Part 2.en.srt" does not attach itself to "Movie (2026)".
    public static ExternalSubtitleFile? TryParse(string videoFileName, string subtitleFilePath)
    {
        var subtitleFileName = Path.GetFileName(subtitleFilePath);
        var codec = CodecForExtension(subtitleFileName);
        if (codec == null) return null;

        var videoBase = Path.GetFileNameWithoutExtension(videoFileName);
        if (string.IsNullOrEmpty(videoBase)) return null;

        var subtitleBase = Path.GetFileNameWithoutExtension(subtitleFileName);

        string suffix;
        if (subtitleBase.Equals(videoBase, StringComparison.OrdinalIgnoreCase))
        {
            suffix = string.Empty;
        }
        else if (subtitleBase.Length > videoBase.Length + 1
            && subtitleBase.StartsWith(videoBase, StringComparison.OrdinalIgnoreCase)
            && subtitleBase[videoBase.Length] == '.')
        {
            suffix = subtitleBase[(videoBase.Length + 1)..];
        }
        else
        {
            return null;
        }

        string? language = null;
        var isForced = false;
        var isSdh = false;

        foreach (var segment in suffix.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ForcedMarkers.Contains(segment)) isForced = true;
            else if (SdhMarkers.Contains(segment)) isSdh = true;
            else if (language == null && LanguageSegment.IsMatch(segment)) language = segment.ToLowerInvariant();
        }

        // The picker shows Title in preference to Language, so a bare "SDH"
        // would hide the language and make two SDH sidecars in different
        // languages read identically. Carrying the language into the title keeps
        // them apart without the client having to change how it labels a track.
        var title = isSdh
            ? (language == null ? "SDH" : $"{language.ToUpperInvariant()} SDH")
            : null;

        return new ExternalSubtitleFile(subtitleFilePath, codec, language, title, isForced, isSdh);
    }
}
