using Vora.Application.Analysis.Results;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Analysis;

public class ChapterMarkerResult
{
    public required List<DetectedMarker> Markers { get; init; }
    public bool CoversIntro { get; init; }
    public bool CoversCredits { get; init; }

    // The chapter layer only supersedes silence/black detection when it can cover
    // every marker type the library still wants. A partial cover (intro chapter but
    // no credits chapter) falls through to a full decode rather than persisting
    // half the markers and skipping the rest.
    public bool Covers(bool detectIntro, bool detectCredits) =>
        Markers.Count > 0
        && (!detectIntro || CoversIntro)
        && (!detectCredits || CoversCredits);
}

public static class ChapterMarkerMapper
{
    private static readonly TimeSpan IntroSearchWindow = TimeSpan.FromMinutes(8);
    private const double CreditsSearchStartFraction = 0.4;

    private static readonly string[] RecapTitles = { "recap", "previously" };
    private static readonly string[] IntroTitles = { "intro", "opening", "main title", "title sequence", "op credit" };
    private static readonly string[] PreviewTitles = { "preview", "next time", "next episode", "next on", "sneak peek" };
    private static readonly string[] CreditsTitles = { "credit", "ending", "outro", "closing", "end title", "end card" };

    public static ChapterMarkerResult Map(
        IReadOnlyList<MediaChapter> chapters,
        TimeSpan duration,
        bool isEpisode,
        bool detectIntro,
        bool detectCredits,
        bool detectPreview)
    {
        var markers = new List<DetectedMarker>();
        if (duration <= TimeSpan.Zero || chapters.Count == 0)
        {
            return new ChapterMarkerResult { Markers = markers };
        }

        var creditsMinStart = TimeSpan.FromSeconds(duration.TotalSeconds * CreditsSearchStartFraction);

        foreach (var chapter in chapters)
        {
            var start = chapter.Start;
            var end = chapter.End;
            if (end <= start || start < TimeSpan.Zero || end > duration) continue;

            var kind = Classify(chapter.Title);
            if (kind == null) continue;

            switch (kind)
            {
                case MarkerType.Recap when detectIntro && isEpisode && start <= IntroSearchWindow:
                    markers.Add(new DetectedMarker { Type = MarkerType.Recap, Start = start, End = end });
                    break;
                case MarkerType.Intro when detectIntro && start <= IntroSearchWindow:
                    markers.Add(new DetectedMarker { Type = MarkerType.Intro, Start = start, End = end });
                    break;
                case MarkerType.Credits when detectCredits && start >= creditsMinStart:
                    markers.Add(new DetectedMarker { Type = MarkerType.Credits, Start = start, End = end });
                    break;
                case MarkerType.Preview when detectPreview && isEpisode && start >= creditsMinStart:
                    markers.Add(new DetectedMarker { Type = MarkerType.Preview, Start = start, End = end });
                    break;
            }
        }

        var deduped = markers
            .GroupBy(m => m.Type)
            .Select(g => g.OrderBy(m => m.Start).First())
            .OrderBy(m => m.Start)
            .ToList();

        return new ChapterMarkerResult
        {
            Markers = deduped,
            CoversIntro = deduped.Any(m => m.Type == MarkerType.Intro),
            CoversCredits = deduped.Any(m => m.Type == MarkerType.Credits)
        };
    }

    private static MarkerType? Classify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var t = title.Trim().ToLowerInvariant();

        // Order matters: "opening credits" is an intro, not the credits roll, so the
        // intro check must win over the credits check; "next episode preview" is a
        // preview, not credits, so preview wins too.
        if (ContainsAny(t, RecapTitles)) return MarkerType.Recap;
        if (ContainsAny(t, IntroTitles)) return MarkerType.Intro;
        if (ContainsAny(t, PreviewTitles)) return MarkerType.Preview;
        if (ContainsAny(t, CreditsTitles)) return MarkerType.Credits;
        return null;
    }

    private static bool ContainsAny(string text, string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
