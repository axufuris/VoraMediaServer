using System.Numerics;

namespace Vora.Application.Analysis;

public readonly record struct FingerprintMatch(int StartIndexA, int EndIndexA)
{
    public int LengthPoints => EndIndexA - StartIndexA + 1;
}

public interface IAudioFingerprintComparer
{
    FingerprintMatch? FindSharedSegment(uint[] a, uint[] b, int maxBitError, int gapTolerance, int minRunPoints);
}

// Locates the longest run of near-identical Chromaprint points shared by two
// fingerprints — the recurring theme (intro) two episodes have in common. Pure
// integer math: no audio, no I/O. Each point is a 32-bit Chromaprint value; two
// points "match" when their Hamming distance is within maxBitError.
public class AudioFingerprintComparer : IAudioFingerprintComparer
{
    // A single very common value would make offset voting quadratic; cap how many
    // of its positions vote. Real Chromaprint output is diverse, so this only
    // guards against a degenerate constant stretch (e.g. a long silence).
    private const int MaxIndicesPerValue = 64;

    public FingerprintMatch? FindSharedSegment(uint[] a, uint[] b, int maxBitError, int gapTolerance, int minRunPoints)
    {
        if (a.Length == 0 || b.Length == 0) return null;

        var offsets = FindCandidateOffsets(a, b);
        if (offsets.Count == 0) return null;

        FingerprintMatch? best = null;
        var bestLength = 0;

        foreach (var offset in offsets)
        {
            var run = LongestRunAtOffset(a, b, offset, maxBitError, gapTolerance);
            if (run == null) continue;
            if (run.Value.LengthPoints > bestLength)
            {
                bestLength = run.Value.LengthPoints;
                best = run;
            }
        }

        if (best == null || bestLength < minRunPoints) return null;
        return best;
    }

    // Vote for the index offset (i - j) that aligns the two fingerprints, using
    // exact-value matches. The recurring theme contributes many identical points,
    // so its true offset dominates the tally; return the top few to expand.
    private static List<int> FindCandidateOffsets(uint[] a, uint[] b)
    {
        var indexB = new Dictionary<uint, List<int>>();
        for (var j = 0; j < b.Length; j++)
        {
            if (!indexB.TryGetValue(b[j], out var list))
            {
                list = new List<int>();
                indexB[b[j]] = list;
            }
            if (list.Count < MaxIndicesPerValue) list.Add(j);
        }

        var votes = new Dictionary<int, int>();
        for (var i = 0; i < a.Length; i++)
        {
            if (!indexB.TryGetValue(a[i], out var matches)) continue;
            foreach (var j in matches)
            {
                var offset = i - j;
                votes.TryGetValue(offset, out var count);
                votes[offset] = count + 1;
            }
        }

        return votes
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    // Walk both fingerprints aligned by the offset, tracking the longest contiguous
    // stretch of matching points. Up to gapTolerance consecutive misses are bridged
    // (a brief dip mid-theme shouldn't split the run); more ends it.
    private static FingerprintMatch? LongestRunAtOffset(uint[] a, uint[] b, int offset, int maxBitError, int gapTolerance)
    {
        var startI = Math.Max(0, offset);
        var endI = Math.Min(a.Length - 1, b.Length - 1 + offset);

        FingerprintMatch? best = null;
        var bestLength = 0;
        var runStart = -1;
        var lastMatch = -1;
        var misses = 0;

        for (var i = startI; i <= endI; i++)
        {
            var j = i - offset;
            var matched = BitOperations.PopCount(a[i] ^ b[j]) <= maxBitError;

            if (matched)
            {
                if (runStart == -1) runStart = i;
                lastMatch = i;
                misses = 0;
            }
            else if (runStart != -1)
            {
                misses++;
                if (misses > gapTolerance)
                {
                    var length = lastMatch - runStart + 1;
                    if (length > bestLength)
                    {
                        bestLength = length;
                        best = new FingerprintMatch(runStart, lastMatch);
                    }
                    runStart = -1;
                    lastMatch = -1;
                    misses = 0;
                }
            }
        }

        if (runStart != -1)
        {
            var length = lastMatch - runStart + 1;
            if (length > bestLength)
            {
                best = new FingerprintMatch(runStart, lastMatch);
            }
        }

        return best;
    }
}
