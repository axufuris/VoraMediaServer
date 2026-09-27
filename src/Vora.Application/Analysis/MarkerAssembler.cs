using Vora.Application.Analysis.Results;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Analysis;

public class DetectedMarker
{
    public MarkerType Type { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public int Order { get; set; }
}

public class MarkerAssemblerInput
{
    public required TimeSpan Duration { get; init; }
    public required List<DetectedInterval> SilenceIntervals { get; init; }
    public required List<DetectedInterval> BlackIntervals { get; init; }
    public bool ExpectsMidCreditsStinger { get; init; }
    public bool ExpectsPostCreditsStinger { get; init; }
    public bool IsEpisode { get; init; }
    public bool DetectIntro { get; init; } = true;
    public bool DetectCredits { get; init; } = true;
}

public interface IMarkerAssembler
{
    List<DetectedMarker> Assemble(MarkerAssemblerInput input);
}

public class MarkerAssembler : IMarkerAssembler
{
    // Head window the intro/recap search reads from, and the fraction of runtime
    // the credits search starts at — exposed so the analyzer can decode only these
    // regions instead of the whole file (everything between is read by nothing).
    public static readonly TimeSpan IntroWindow = TimeSpan.FromMinutes(8);
    public const double CreditsSearchStartFraction = 0.6;
    private static readonly TimeSpan EpisodeRecapWindow = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CreditsRollMinLength = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinStingerLength = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan BoundaryProximity = TimeSpan.FromSeconds(3);
    // A real title sequence / "previously on" runs longer than this. A shorter
    // black+silence blip at the very start is a studio-logo ident (HBO, etc.), not
    // an intro — emitting it just flashes a "Skip Intro" button at 0:00.
    private static readonly TimeSpan MinIntroDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinRecapDuration = TimeSpan.FromSeconds(5);
    // An intro (recap + title sequence) is never longer than this. A joint gap that
    // would push the intro past it is a mid-episode scene fade, not the end of the
    // opening — the old "last gap within 8 minutes" logic grabbed those and marked
    // the first 7-8 minutes as intro. Beyond this cap, emit no intro rather than a
    // wrong one (a fingerprint/chapter tier is where an accurate intro comes from).
    private static readonly TimeSpan MaxIntroDuration = TimeSpan.FromSeconds(150);
    // Credits run to (near) the end, so the credits-start gap leaves only a short
    // tail. A gap that leaves more than this behind it is an act break / scene
    // fade, not the credits roll — skip past it. Episodes get a tight absolute cap
    // (TV credits are short, and a late act break leaves an act-length tail of a
    // few minutes that a runtime fraction wouldn't catch on a longer episode);
    // movies keep the fraction since their credits are legitimately long.
    private static readonly TimeSpan MaxEpisodeCreditsRoll = TimeSpan.FromMinutes(4);
    private const double MaxCreditsRollFraction = 0.2;
    // Credit cards with dense text briefly break the black background; black runs
    // split by less than this are one credits region, not separate events.
    private static readonly TimeSpan CreditsBlackMergeGap = TimeSpan.FromSeconds(15);

    public List<DetectedMarker> Assemble(MarkerAssemblerInput input)
    {
        var markers = new List<DetectedMarker>();
        if (input.Duration <= TimeSpan.Zero) return markers;

        // Intro/recap read silence∩black joint gaps; credits read black frames
        // alone (they roll over black even with music). Don't bail when there are no
        // joint gaps — a music-over-black credits roll has none yet is still there.
        var jointGaps = FindJointSilenceAndBlackGaps(input.SilenceIntervals, input.BlackIntervals);

        DetectedMarker? introMarker = null;
        if (input.DetectIntro)
        {
            introMarker = FindIntroMarker(jointGaps, input);
            if (introMarker != null) markers.Add(introMarker);

            var recapMarker = FindRecapMarker(jointGaps, input, introMarker);
            if (recapMarker != null) markers.Add(recapMarker);
        }

        var creditsRollStart = input.DetectCredits ? FindCreditsRollStart(input) : null;
        if (creditsRollStart != null)
        {
            markers.Add(new DetectedMarker
            {
                Type = MarkerType.Credits,
                Start = creditsRollStart.Value,
                End = input.Duration
            });

            if (!input.IsEpisode)
            {
                var expectedStingers = (input.ExpectsMidCreditsStinger ? 1 : 0) + (input.ExpectsPostCreditsStinger ? 1 : 0);
                if (expectedStingers > 0)
                {
                    var stingers = FindCreditsScenes(jointGaps, creditsRollStart.Value, input.Duration)
                        .Take(expectedStingers)
                        .ToList();
                    for (var i = 0; i < stingers.Count; i++)
                    {
                        stingers[i].Order = i + 1;
                        markers.Add(stingers[i]);
                    }
                }
            }
            else
            {
                // Previews are always computed and stored; the per-library toggle
                // only controls whether the player surfaces them (a "Skip Preview"
                // button), so turning it on/off never needs a re-analyze.
                var preview = FindEpisodePreview(jointGaps, creditsRollStart.Value, input.Duration);
                if (preview != null) markers.Add(preview);
            }
        }

        return markers.OrderBy(m => m.Start).ToList();
    }

    private static List<DetectedInterval> FindJointSilenceAndBlackGaps(List<DetectedInterval> silence, List<DetectedInterval> black)
    {
        var joint = new List<DetectedInterval>();
        foreach (var s in silence)
        {
            foreach (var b in black)
            {
                var start = s.Start > b.Start ? s.Start : b.Start;
                var end = s.End < b.End ? s.End : b.End;
                if (end > start)
                {
                    joint.Add(new DetectedInterval { Start = start, End = end });
                }
            }
        }
        return joint.OrderBy(g => g.Start).ToList();
    }

    private static DetectedMarker? FindIntroMarker(List<DetectedInterval> jointGaps, MarkerAssemblerInput input)
    {
        // Intro means "skip past the opening into the real content". When an episode
        // has a "Previously on..." recap followed by a title sequence, both produce
        // joint silence+black gaps, so use the LAST gap that still keeps the intro
        // within MaxIntroDuration — that covers recap+theme without letting a later
        // scene fade balloon the intro to several minutes. If every gap sits past the
        // cap there's no detectable intro here (emit none rather than a wrong one).
        var introCandidates = jointGaps
            .Where(g => g.End <= MaxIntroDuration)
            .ToList();
        if (introCandidates.Count == 0) return null;

        var introEnd = introCandidates[^1].End;
        if (introEnd < MinIntroDuration) return null;

        return new DetectedMarker
        {
            Type = MarkerType.Intro,
            Start = TimeSpan.Zero,
            End = introEnd
        };
    }

    private static DetectedMarker? FindRecapMarker(List<DetectedInterval> jointGaps, MarkerAssemblerInput input, DetectedMarker? intro)
    {
        if (!input.IsEpisode || intro == null) return null;

        // Recap is a finer-grained subset of the intro: when the player offers
        // "Skip Recap" alongside "Skip Intro", recap covers just the "Previously on..."
        // segment. We only emit a recap marker if there's an early gap (within
        // EpisodeRecapWindow) that ENDS strictly before the intro's end — i.e. the
        // episode has multiple gaps and the first one bounds the recap.
        var earlyGap = jointGaps.FirstOrDefault(g =>
            g.Start <= EpisodeRecapWindow && g.End < intro.End);
        if (earlyGap == null) return null;
        if (earlyGap.End < MinRecapDuration) return null;

        return new DetectedMarker
        {
            Type = MarkerType.Recap,
            Start = TimeSpan.Zero,
            End = earlyGap.End
        };
    }

    private static TimeSpan? FindCreditsRollStart(MarkerAssemblerInput input)
    {
        var minStart = TimeSpan.FromSeconds(input.Duration.TotalSeconds * CreditsSearchStartFraction);

        // Credits are detected from BLACK frames alone, not silence∩black. Unlike
        // the intro, a credits roll is reliably on a black background even when the
        // theme music plays over it — requiring silence too meant a black-with-music
        // credits roll (HBO shows, etc.) produced no joint gap and went undetected.
        // Dense credit cards briefly break the black, so merge black runs separated
        // by a short gap into one region.
        var tailBlack = input.BlackIntervals
            .Where(b => b.End >= minStart)
            .OrderBy(b => b.Start)
            .ToList();
        var creditsCandidates = MergeCloseIntervals(tailBlack, CreditsBlackMergeGap)
            .Where(g => g.Start >= minStart)
            .OrderBy(g => g.Start)
            .ToList();

        if (creditsCandidates.Count == 0) return null;

        // Pick the earliest black region that leaves only a plausible credits-length
        // tail. Act breaks / scene fades earlier in the episode leave a longer tail,
        // so skipping over-long candidates lands on the real credits roll near the
        // end. Episodes emit no credits when nothing fits (better than mislabeling
        // minutes of content); movies keep the fallback since their credits are long.
        var maxTail = input.IsEpisode
            ? MaxEpisodeCreditsRoll
            : TimeSpan.FromSeconds(input.Duration.TotalSeconds * MaxCreditsRollFraction);
        var creditsStart = creditsCandidates.FirstOrDefault(g => input.Duration - g.Start <= maxTail);
        if (creditsStart == null)
        {
            if (input.IsEpisode) return null;
            creditsStart = creditsCandidates[^1];
        }

        return creditsStart.Start;
    }

    private static List<DetectedInterval> MergeCloseIntervals(List<DetectedInterval> sorted, TimeSpan maxGap)
    {
        var merged = new List<DetectedInterval>();
        foreach (var interval in sorted)
        {
            if (merged.Count > 0 && interval.Start - merged[^1].End <= maxGap)
            {
                if (interval.End > merged[^1].End) merged[^1].End = interval.End;
            }
            else
            {
                merged.Add(new DetectedInterval { Start = interval.Start, End = interval.End });
            }
        }
        return merged;
    }

    private static IEnumerable<DetectedMarker> FindCreditsScenes(List<DetectedInterval> jointGaps, TimeSpan creditsStart, TimeSpan duration)
    {
        var gapsInCredits = jointGaps
            .Where(g => g.Start >= creditsStart && g.End <= duration)
            .OrderBy(g => g.Start)
            .ToList();

        for (var i = 0; i < gapsInCredits.Count - 1; i++)
        {
            var sceneStart = gapsInCredits[i].End;
            var sceneEnd = gapsInCredits[i + 1].Start;
            if (sceneEnd - sceneStart < MinStingerLength) continue;
            if (sceneStart <= creditsStart + BoundaryProximity) continue;

            yield return new DetectedMarker
            {
                Type = MarkerType.CreditsScene,
                Start = sceneStart,
                End = sceneEnd
            };
        }
    }

    private static DetectedMarker? FindEpisodePreview(List<DetectedInterval> jointGaps, TimeSpan creditsStart, TimeSpan duration)
    {
        var gapsInCredits = jointGaps
            .Where(g => g.Start >= creditsStart && g.End <= duration)
            .OrderBy(g => g.Start)
            .ToList();

        for (var i = 0; i < gapsInCredits.Count - 1; i++)
        {
            var sceneStart = gapsInCredits[i].End;
            var sceneEnd = gapsInCredits[i + 1].Start;
            if (sceneEnd - sceneStart < MinStingerLength) continue;
            if (sceneStart <= creditsStart + BoundaryProximity) continue;

            return new DetectedMarker
            {
                Type = MarkerType.Preview,
                Start = sceneStart,
                End = sceneEnd
            };
        }

        // A trailing gap well after the credits start marks a "next time on…"
        // preview running to the end. Require it to be clear of the credits
        // boundary, otherwise the credits roll's own opening gap would get
        // mislabeled as a preview.
        var lastGap = gapsInCredits.LastOrDefault();
        if (lastGap != null
            && lastGap.Start > creditsStart + BoundaryProximity
            && duration - lastGap.End >= CreditsRollMinLength)
        {
            return new DetectedMarker
            {
                Type = MarkerType.Preview,
                Start = lastGap.End,
                End = duration
            };
        }

        return null;
    }
}
