using Vora.Domain.Entities.Media;

namespace Vora.Application.Analysis;

// Resolves the final marker set from the three detection tiers by precedence:
// chapters (authoritative) > audio-fingerprint (intro only) > silence/black.
// Each single-instance marker type takes the highest-priority tier that produced
// it; CreditsScene markers (movies) only ever come from silence/black.
public static class MarkerMerge
{
    public static List<DetectedMarker> Resolve(
        IReadOnlyList<DetectedMarker> chapterMarkers,
        DetectedMarker? fingerprintIntro,
        IReadOnlyList<DetectedMarker> silenceBlackMarkers,
        bool detectIntro)
    {
        var result = new List<DetectedMarker>();

        var intro = First(chapterMarkers, MarkerType.Intro);
        if (intro == null && detectIntro) intro = fingerprintIntro;
        intro ??= First(silenceBlackMarkers, MarkerType.Intro);
        if (intro != null) result.Add(intro);

        foreach (var type in new[] { MarkerType.Recap, MarkerType.Credits, MarkerType.Preview })
        {
            var marker = First(chapterMarkers, type) ?? First(silenceBlackMarkers, type);
            if (marker != null) result.Add(marker);
        }

        result.AddRange(silenceBlackMarkers.Where(m => m.Type == MarkerType.CreditsScene));

        return result.OrderBy(m => m.Start).ToList();
    }

    private static DetectedMarker? First(IReadOnlyList<DetectedMarker> markers, MarkerType type)
    {
        foreach (var marker in markers)
        {
            if (marker.Type == type) return marker;
        }
        return null;
    }
}
