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
    public List<PictureSample> PictureSamples { get; init; } = new();
    public List<TimeSpan> ChapterStarts { get; init; } = new();
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
    private static readonly TimeSpan MaxStingerLength = TimeSpan.FromMinutes(5);
    private const double MaxStingerShareOfCredits = 0.5;
    private const double CrawlMaxSaturation = 1.5;
    private const double CrawlMinPeakLuma = 140;
    private const double CrawlMaxLowLuma = 24;
    private const double CrawlMaxMeanLuma = 50;
    private const double BlackMaxPeakLuma = 40;
    private static readonly TimeSpan CrawlOpeningWindow = TimeSpan.FromSeconds(30);
    private const int CrawlOpeningMinSamples = 10;
    private const double CrawlOpeningShare = 0.75;
    private const double CrawlToEndMaxPictureShare = 0.4;
    private const int MovieBeforeCrawlMinSamples = 60;
    private const double MovieBeforeCrawlMaxCrawlShare = 0.2;
    private static readonly TimeSpan PictureSampleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CrawlLeadInSlack = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxTitleSequenceLength = TimeSpan.FromSeconds(135);
    private static readonly TimeSpan MinFadeOutLength = TimeSpan.FromSeconds(2);
    private const double SceneMinPictureShare = 0.65;
    private static readonly TimeSpan SceneJoinGap = TimeSpan.FromSeconds(8);
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

        var crawl = input.DetectCredits && !input.IsEpisode ? FindCreditsCrawl(input) : null;
        var creditsRollStart = crawl?.CreditsStart ?? (input.DetectCredits ? FindCreditsRollStart(input) : null);
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
                var candidates = crawl != null
                    ? FindCreditsScenesAroundCrawl(input, crawl)
                    : FindCreditsScenes(jointGaps, creditsRollStart.Value, input.Duration).ToList();
                var stingers = PickCreditsScenes(candidates, input.ExpectsMidCreditsStinger, input.ExpectsPostCreditsStinger);
                for (var i = 0; i < stingers.Count; i++)
                {
                    stingers[i].Order = i + 1;
                    markers.Add(stingers[i]);
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

    private static CreditsCrawl? FindCreditsCrawl(MarkerAssemblerInput input)
    {
        var minStart = TimeSpan.FromSeconds(input.Duration.TotalSeconds * CreditsSearchStartFraction);
        var maxTail = TimeSpan.FromSeconds(input.Duration.TotalSeconds * MaxCreditsRollFraction);
        var samples = input.PictureSamples
            .Where(s => s.Time >= minStart && s.Time <= input.Duration)
            .OrderBy(s => s.Time)
            .ToList();
        var shots = samples.Select(Classify).ToList();

        var picturesFromHere = new int[shots.Count + 1];
        for (var i = shots.Count - 1; i >= 0; i--)
        {
            picturesFromHere[i] = picturesFromHere[i + 1] + (shots[i] == Shot.Picture ? 1 : 0);
        }

        for (var i = 0; i < shots.Count; i++)
        {
            if (shots[i] != Shot.Crawl || input.Duration - samples[i].Time > maxTail) continue;
            if (!OpensACrawl(samples, shots, i)) continue;
            if (picturesFromHere[i] > (shots.Count - i) * CrawlToEndMaxPictureShare) continue;

            var crawlStart = StartOfBlackLeadingInto(samples[i].Time, input.BlackIntervals);
            var creditsStart = StartOfTitleSequence(crawlStart, minStart, input);
            if (MovieLooksLikeCredits(samples, shots, creditsStart)) return null;

            return new CreditsCrawl(creditsStart, crawlStart);
        }

        return null;
    }

    private static Shot Classify(PictureSample sample)
    {
        if (sample.PeakLuma < BlackMaxPeakLuma) return Shot.Black;

        return sample.Saturation <= CrawlMaxSaturation
            && sample.PeakLuma >= CrawlMinPeakLuma
            && sample.LowLuma <= CrawlMaxLowLuma
            && sample.MeanLuma <= CrawlMaxMeanLuma
                ? Shot.Crawl
                : Shot.Picture;
    }

    private static bool OpensACrawl(List<PictureSample> samples, List<Shot> shots, int first)
    {
        int crawl = 0, picture = 0;
        var windowEnd = samples[first].Time + CrawlOpeningWindow;
        for (var i = first; i < samples.Count && samples[i].Time < windowEnd; i++)
        {
            if (shots[i] == Shot.Crawl) crawl++;
            else if (shots[i] == Shot.Picture) picture++;
        }

        return crawl >= CrawlOpeningMinSamples && crawl >= (crawl + picture) * CrawlOpeningShare;
    }

    private static TimeSpan StartOfBlackLeadingInto(TimeSpan crawlStart, List<DetectedInterval> blackIntervals)
    {
        var start = crawlStart;
        foreach (var black in blackIntervals)
        {
            if (black.Start <= crawlStart + PictureSampleInterval
                && black.End >= crawlStart - CrawlLeadInSlack
                && black.Start < start)
            {
                start = black.Start;
            }
        }
        return start;
    }

    private static TimeSpan StartOfTitleSequence(TimeSpan crawlStart, TimeSpan minStart, MarkerAssemblerInput input)
    {
        var earliest = crawlStart - MaxTitleSequenceLength;
        if (earliest < minStart) earliest = minStart;

        return input.BlackIntervals
            .Where(b => b.Duration >= MinFadeOutLength)
            .Select(b => b.Start)
            .Concat(input.ChapterStarts)
            .Where(t => t >= earliest && t < crawlStart)
            .DefaultIfEmpty(crawlStart)
            .Min();
    }

    private static bool MovieLooksLikeCredits(List<PictureSample> samples, List<Shot> shots, TimeSpan creditsStart)
    {
        int crawl = 0, picture = 0;
        for (var i = 0; i < samples.Count && samples[i].Time < creditsStart; i++)
        {
            if (shots[i] == Shot.Crawl) crawl++;
            else if (shots[i] == Shot.Picture) picture++;
        }

        return crawl + picture >= MovieBeforeCrawlMinSamples
            && crawl > (crawl + picture) * MovieBeforeCrawlMaxCrawlShare;
    }

    private static List<DetectedMarker> FindCreditsScenesAroundCrawl(MarkerAssemblerInput input, CreditsCrawl crawl)
    {
        var cuts = input.BlackIntervals
            .Where(b => b.End > crawl.CreditsStart && b.Start < input.Duration)
            .Select(b => (b.Start, b.End))
            .Concat(input.ChapterStarts
                .Where(c => c > crawl.CreditsStart && c < input.Duration)
                .Select(c => (Start: c, End: c)))
            .OrderBy(c => c.Start)
            .ToList();

        var segments = new List<CreditsSegment>();
        var from = crawl.CreditsStart;
        foreach (var (start, end) in cuts)
        {
            if (start > from) segments.Add(MeasureSegment(from, start, input.PictureSamples));
            if (end > from) from = end;
        }
        if (input.Duration > from) segments.Add(MeasureSegment(from, input.Duration, input.PictureSamples));

        var merged = new List<CreditsSegment>();
        foreach (var segment in segments.Where(s => s.Samples > 0))
        {
            if (merged.Count > 0 && JoinsPreviousScene(merged[^1], segment, crawl.CrawlStart))
            {
                var previous = merged[^1];
                merged[^1] = new CreditsSegment(previous.Start, segment.End, previous.Samples + segment.Samples, previous.Pictures + segment.Pictures);
                continue;
            }
            merged.Add(segment);
        }

        var creditsLength = input.Duration - crawl.CreditsStart;
        return merged
            .Skip(1)
            .Where(s => s.IsScene
                && s.End >= crawl.CrawlStart - CrawlLeadInSlack
                && CouldBeACreditsScene(s.Start, s.End, crawl.CreditsStart, creditsLength))
            .Select(s => new DetectedMarker { Type = MarkerType.CreditsScene, Start = s.Start, End = s.End })
            .ToList();
    }

    private static CreditsSegment MeasureSegment(TimeSpan start, TimeSpan end, List<PictureSample> samples)
    {
        int count = 0, pictures = 0;
        foreach (var sample in samples)
        {
            if (sample.Time <= start || sample.Time >= end) continue;
            count++;
            if (Classify(sample) == Shot.Picture) pictures++;
        }
        return new CreditsSegment(start, end, count, pictures);
    }

    private static bool JoinsPreviousScene(CreditsSegment previous, CreditsSegment next, TimeSpan crawlStart) =>
        previous.Start > crawlStart
        && previous.IsScene
        && next.IsScene
        && next.Start - previous.End <= SceneJoinGap;

    private enum Shot
    {
        Black,
        Crawl,
        Picture
    }

    private sealed record CreditsCrawl(TimeSpan CreditsStart, TimeSpan CrawlStart);

    private sealed record CreditsSegment(TimeSpan Start, TimeSpan End, int Samples, int Pictures)
    {
        public bool IsScene => Samples > 0 && Pictures >= Samples * SceneMinPictureShare;
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
        var creditsLength = duration - creditsStart;

        for (var i = 0; i < gapsInCredits.Count - 1; i++)
        {
            var sceneStart = gapsInCredits[i].End;
            var sceneEnd = gapsInCredits[i + 1].Start;
            if (!CouldBeACreditsScene(sceneStart, sceneEnd, creditsStart, creditsLength)) continue;

            yield return new DetectedMarker
            {
                Type = MarkerType.CreditsScene,
                Start = sceneStart,
                End = sceneEnd
            };
        }
    }

    private static bool CouldBeACreditsScene(TimeSpan start, TimeSpan end, TimeSpan creditsStart, TimeSpan creditsLength)
    {
        var length = end - start;
        return length >= MinStingerLength
            && start > creditsStart + BoundaryProximity
            && length <= MaxStingerLength
            && length.TotalSeconds <= creditsLength.TotalSeconds * MaxStingerShareOfCredits;
    }

    private static List<DetectedMarker> PickCreditsScenes(List<DetectedMarker> candidates, bool expectsMid, bool expectsPost)
    {
        if (candidates.Count == 0) return [];
        if (expectsMid && expectsPost) return candidates.Count == 1 ? [candidates[0]] : [candidates[0], candidates[^1]];
        if (expectsPost) return [candidates[^1]];
        if (expectsMid) return [candidates[0]];
        return [];
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
