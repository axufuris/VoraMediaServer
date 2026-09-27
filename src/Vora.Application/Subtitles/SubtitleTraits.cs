using System.Text.RegularExpressions;

namespace Vora.Application.Subtitles;

// Most containers never set disposition.hearing_impaired, but the muxer almost
// always wrote the fact into the track title ("English SDH", "eng [CC]"). This
// reads that, so an SDH track is labelled as one whether or not the flag is set.
//
// A bare "hi" is deliberately NOT a marker here even though it is one in a
// sidecar file name: a file name segment is a machine-written field, a track
// title is free text somebody typed, and "Hi" reads as a greeting far more often
// than as a marker. "hoh" and the spelled-out form cover that case instead.
public static class SubtitleTraits
{
    private static readonly Regex HearingImpairedTitle = new(
        @"(^|[\s\.\-_\[\(\/])(sdh|cc|hoh|hearing[\s\-_]?impaired)([\s\.\-_\]\)\/]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TitleSuggestsHearingImpaired(string? title) =>
        !string.IsNullOrWhiteSpace(title) && HearingImpairedTitle.IsMatch(title);
}
