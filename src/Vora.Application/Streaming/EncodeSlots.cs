namespace Vora.Application.Streaming;

/// <summary>One video encode that is running now.</summary>
/// <param name="OnGpu">Whether it encodes on the GPU. A GPU encode holds one of the card's encoder sessions.</param>
public sealed record RunningEncode(bool OnGpu);

/// <summary>
/// Somewhere video encodes run: playback transcodes, live TV sessions, DVR conversions. Each keeps its own
/// record of what it is running, so there is nothing to keep in step: the count is read when a start asks.
/// </summary>
public interface IEncodeSource
{
    /// <param name="except">The key of the encode asking, so a restart (a seek, a channel change, a retry on the
    /// CPU) is not counted against itself.</param>
    IReadOnlyList<RunningEncode> RunningEncodes(Guid except);
}

/// <param name="OnGpu">Whether the encode goes to the GPU.</param>
/// <param name="Refusal">Why there is no room for it, or null.</param>
public sealed record EncodeSlot(bool OnGpu, string? Refusal);

/// <summary>
/// The server's limits on how many video encodes run at once (Maximum GPU and CPU transcodes), shared by
/// everything that encodes: a hardware encoder has a fixed number of sessions, and a live TV channel, a
/// movie and a recording each take one.
/// </summary>
public interface IEncodeSlots
{
    /// <summary>
    /// Holds off every other start while this one picks a slot and records itself where it is counted,
    /// so two starts at the same moment can't both take the last slot. Dispose once the encode is registered.
    /// </summary>
    Task<IDisposable> HoldAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The GPU while it has room, then the CPU while it has, then a refusal. Counts every encode running
    /// except the one <paramref name="key"/> owns. Call with the hold taken.
    /// </summary>
    EncodeSlot Assign(Guid key, bool gpuReady, int maxGpu, int maxCpu);
}

/// <summary>An encode was needed and the server is already running as many as it is set to.</summary>
public sealed class EncodeLimitReachedException(string message) : Exception(message);
