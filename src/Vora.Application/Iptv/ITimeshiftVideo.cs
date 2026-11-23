namespace Vora.Application.Iptv;

public sealed record TimeshiftVideoArguments(IReadOnlyList<string> BeforeInput, IReadOnlyList<string> Video, string? Encoder, bool OnGpu)
{
    public static TimeshiftVideoArguments Copy { get; } = new([], ["-c:v", "copy"], null, false);

    public bool Encodes => Encoder is not null;
}

public interface ITimeshiftVideo
{
    Task<LiveVideoFormat?> ReadFormatAsync(string segmentPath, CancellationToken cancellationToken = default);
    /// <summary>
    /// How to encode a viewer's video: on the GPU while the server's limit has room, otherwise on the CPU
    /// (<paramref name="onCpu"/> asks for the CPU, as when the GPU just failed). Takes a slot, so call it
    /// with <see cref="Vora.Application.Streaming.IEncodeSlots.HoldAsync"/> held until the session is registered.
    /// </summary>
    /// <exception cref="Vora.Application.Streaming.EncodeLimitReachedException">There is no slot for it.</exception>
    Task<TimeshiftVideoArguments> EncodeArgumentsAsync(LiveVideoEncode encode, Guid viewerKey, bool onCpu, CancellationToken cancellationToken = default);
}
