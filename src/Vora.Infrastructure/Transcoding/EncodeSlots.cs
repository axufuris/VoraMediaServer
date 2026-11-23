using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

/// <summary>
/// Reads what every <see cref="IEncodeSource"/> is running and applies <see cref="TranscodeSlots"/> to it, so a
/// playback transcode, a live TV channel and a DVR conversion are all held to the same limits.
/// </summary>
public sealed class EncodeSlots(IEnumerable<IEncodeSource> sources) : IEncodeSlots
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> HoldAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Hold(_gate);
    }

    public EncodeSlot Assign(Guid key, bool gpuReady, int maxGpu, int maxCpu)
    {
        var others = sources
            .SelectMany(source => source.RunningEncodes(key))
            .Select(running => new TranscodeSlots.Running(EncodesVideo: true, running.OnGpu));
        var slot = TranscodeSlots.Assign(encodesVideo: true, gpuReady, others, maxGpu, maxCpu);
        return new EncodeSlot(slot.OnGpu, slot.Refusal);
    }

    private sealed class Hold(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
