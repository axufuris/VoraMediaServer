namespace Vora.Infrastructure.Transcoding;

internal static class TranscodeSlots
{
    internal sealed record Running(bool EncodesVideo, bool OnGpu);

    internal sealed record Assignment(bool OnGpu, string? Refusal);

    public static Assignment Assign(bool encodesVideo, bool gpuReady, IEnumerable<Running> others, int maxGpu, int maxCpu)
    {
        if (!encodesVideo) return new Assignment(false, null);

        var encoding = others.Where(running => running.EncodesVideo).ToList();
        int onGpu = encoding.Count(running => running.OnGpu);
        int onCpu = encoding.Count - onGpu;

        if (gpuReady && (maxGpu == 0 || onGpu < maxGpu)) return new Assignment(true, null);
        if (maxCpu > 0 && onCpu >= maxCpu) return new Assignment(false, gpuReady ? "Maximum GPU and CPU transcodes reached." : "Maximum CPU transcodes reached.");
        return new Assignment(false, null);
    }
}
