using System.Globalization;
using System.Text.RegularExpressions;
using Vora.Application.Analysis;

namespace Vora.Infrastructure.Analysis;

internal static class WebVttCues
{
    private static readonly Regex Timing = new(
        @"^\s*(?:(?<h1>\d+):)?(?<m1>\d{1,2}):(?<s1>\d{2})[.,](?<f1>\d{3})\s*-->\s*(?:(?<h2>\d+):)?(?<m2>\d{1,2}):(?<s2>\d{2})[.,](?<f2>\d{3})",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public static IReadOnlyList<SubtitleCue> Parse(string vtt)
    {
        var cues = new List<SubtitleCue>();
        var lines = vtt.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var timing = Timing.Match(lines[index]);
            if (!timing.Success) continue;

            var text = new List<string>();
            while (index + 1 < lines.Length && lines[index + 1].Trim().Length > 0)
            {
                index++;
                text.Add(lines[index].Trim());
            }
            cues.Add(new SubtitleCue(Time(timing, "1"), Time(timing, "2"), string.Join(' ', text)));
        }
        return cues;
    }

    private static TimeSpan Time(Match timing, string which)
    {
        var hours = timing.Groups["h" + which];
        return new TimeSpan(
            0,
            hours.Success ? Number(hours.Value) : 0,
            Number(timing.Groups["m" + which].Value),
            Number(timing.Groups["s" + which].Value),
            Number(timing.Groups["f" + which].Value));
    }

    private static int Number(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
}
