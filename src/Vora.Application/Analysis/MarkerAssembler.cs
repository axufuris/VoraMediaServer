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
}

public interface IMarkerAssembler
{
    List<DetectedMarker> Assemble(MarkerAssemblerInput input);
}

public class MarkerAssembler : IMarkerAssembler
{
    private static readonly TimeSpan IntroWindow = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan EpisodeRecapWindow = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CreditsRollMinLength = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinStingerLength = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan BoundaryProximity = TimeSpan.FromSeconds(3);

    public List<DetectedMarker> Assemble(MarkerAssemblerInput input)
    {
        var markers = new List<DetectedMarker>();
        if (input.Duration <= TimeSpan.Zero) return markers;

        var jointGaps = FindJointSilenceAndBlackGaps(input.SilenceIntervals, input.BlackIntervals);
        if (jointGaps.Count == 0) return markers;

        var introMarker = FindIntroMarker(jointGaps, input);
        if (introMarker != null) markers.Add(introMarker);

        var recapMarker = FindRecapMarker(jointGaps, input, introMarker);
        if (recapMarker != null) markers.Add(recapMarker);

        var creditsRollStart = FindCreditsRollStart(jointGaps, input);
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
        DetectedInterval? first = null;
        foreach (var gap in jointGaps)
        {
            if (gap.Start > IntroWindow) break;
            first = gap;
            break;
        }
        if (first == null) return null;

        var introStart = TimeSpan.Zero;
        var introEnd = first.End;
        if (introEnd <= introStart) return null;

        return new DetectedMarker
        {
            Type = MarkerType.Intro,
            Start = introStart,
            End = introEnd
        };
    }

    private static DetectedMarker? FindRecapMarker(List<DetectedInterval> jointGaps, MarkerAssemblerInput input, DetectedMarker? intro)
    {
        if (!input.IsEpisode || intro == null) return null;
        var preIntro = jointGaps.FirstOrDefault(g => g.End <= intro.Start && g.Start <= EpisodeRecapWindow);
        if (preIntro == null) return null;
        if (preIntro.End <= TimeSpan.Zero) return null;
        return new DetectedMarker
        {
            Type = MarkerType.Recap,
            Start = TimeSpan.Zero,
            End = preIntro.End
        };
    }

    private static TimeSpan? FindCreditsRollStart(List<DetectedInterval> jointGaps, MarkerAssemblerInput input)
    {
        var minStart = TimeSpan.FromSeconds(input.Duration.TotalSeconds * 0.6);
        var creditsCandidates = jointGaps
            .Where(g => g.Start >= minStart)
            .OrderBy(g => g.Start)
            .ToList();

        return creditsCandidates.Count > 0 ? creditsCandidates.First().Start : (TimeSpan?)null;
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

        var lastGap = gapsInCredits.LastOrDefault();
        if (lastGap != null && duration - lastGap.End >= CreditsRollMinLength)
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
