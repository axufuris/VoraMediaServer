using Vora.Application.Analysis;

namespace Vora.Application.Tests.Analysis;

public class AudioFingerprintComparerTests
{
    private readonly AudioFingerprintComparer _comparer = new();

    // Deterministic pseudo-random fingerprint values; no shared theme by construction
    // (each index seeded distinctly), so any real overlap must come from an injected run.
    private static uint[] Noise(int count, int seed)
    {
        var values = new uint[count];
        var state = (uint)(seed * 2654435761u + 1);
        for (var i = 0; i < count; i++)
        {
            state = state * 1664525u + 1013904223u;
            values[i] = state;
        }
        return values;
    }

    private static void Inject(uint[] target, int at, uint[] run)
    {
        for (var i = 0; i < run.Length; i++) target[at + i] = run[i];
    }

    [Fact]
    public void Finds_an_identical_run_at_the_same_offset()
    {
        var run = Noise(200, seed: 99);
        var a = Noise(1000, seed: 1);
        var b = Noise(1000, seed: 2);
        Inject(a, 300, run);
        Inject(b, 300, run);

        var match = _comparer.FindSharedSegment(a, b, maxBitError: 6, gapTolerance: 3, minRunPoints: 50);

        match.Should().NotBeNull();
        match!.Value.StartIndexA.Should().Be(300);
        match.Value.EndIndexA.Should().Be(499);
    }

    [Fact]
    public void Finds_the_run_when_it_sits_at_different_offsets_in_each()
    {
        var run = Noise(150, seed: 7);
        var a = Noise(1000, seed: 3);
        var b = Noise(1000, seed: 4);
        Inject(a, 120, run);   // intro after a short cold open in A
        Inject(b, 480, run);   // later in B

        var match = _comparer.FindSharedSegment(a, b, maxBitError: 6, gapTolerance: 3, minRunPoints: 50);

        match.Should().NotBeNull();
        match!.Value.StartIndexA.Should().Be(120);
        match.Value.EndIndexA.Should().Be(269);
    }

    [Fact]
    public void Tolerates_small_bit_errors_within_the_run()
    {
        var run = Noise(200, seed: 11);
        var a = Noise(900, seed: 5);
        var b = Noise(900, seed: 6);
        Inject(a, 200, run);

        // Real intros are bit-identical across episodes with scattered encode noise:
        // flip 2 of 32 bits on every 4th point. Exact anchors still reveal the offset;
        // the noisy points (Hamming 2 <= maxBitError 6) stay inside the expanded run.
        var noisyRun = (uint[])run.Clone();
        for (var i = 0; i < noisyRun.Length; i += 4) noisyRun[i] ^= 0b1010u;
        Inject(b, 200, noisyRun);

        var match = _comparer.FindSharedSegment(a, b, maxBitError: 6, gapTolerance: 3, minRunPoints: 50);

        match.Should().NotBeNull();
        match!.Value.LengthPoints.Should().Be(200);
    }

    [Fact]
    public void Bridges_a_short_gap_inside_the_run()
    {
        var run = Noise(200, seed: 13);
        var a = Noise(900, seed: 8);
        var b = Noise(900, seed: 9);
        Inject(a, 200, run);
        Inject(b, 200, run);
        // Corrupt 2 consecutive points in the middle of B's copy (within gapTolerance=3).
        b[290] = 0xFFFFFFFFu;
        b[291] = 0x00000000u;

        var match = _comparer.FindSharedSegment(a, b, maxBitError: 6, gapTolerance: 3, minRunPoints: 50);

        match.Should().NotBeNull();
        match!.Value.StartIndexA.Should().Be(200);
        match.Value.EndIndexA.Should().Be(399);
    }

    [Fact]
    public void Returns_null_when_no_run_meets_the_minimum_length()
    {
        var run = Noise(20, seed: 21); // only 20 points shared
        var a = Noise(900, seed: 10);
        var b = Noise(900, seed: 11);
        Inject(a, 300, run);
        Inject(b, 300, run);

        var match = _comparer.FindSharedSegment(a, b, maxBitError: 6, gapTolerance: 3, minRunPoints: 50);

        match.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_there_is_no_shared_content()
    {
        var a = Noise(1000, seed: 12);
        var b = Noise(1000, seed: 13);

        var match = _comparer.FindSharedSegment(a, b, maxBitError: 6, gapTolerance: 3, minRunPoints: 50);

        match.Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_empty_fingerprints()
    {
        _comparer.FindSharedSegment(Array.Empty<uint>(), Noise(100, 1), 6, 3, 50).Should().BeNull();
        _comparer.FindSharedSegment(Noise(100, 1), Array.Empty<uint>(), 6, 3, 50).Should().BeNull();
    }
}
