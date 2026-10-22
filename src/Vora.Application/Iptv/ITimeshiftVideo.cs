namespace Vora.Application.Iptv;

public sealed record TimeshiftVideoArguments(IReadOnlyList<string> BeforeInput, IReadOnlyList<string> Video, string? Encoder, bool OnGpu)
{
    public static TimeshiftVideoArguments Copy { get; } = new([], ["-c:v", "copy"], null, false);

    public bool Encodes => Encoder is not null;
}

public interface ITimeshiftVideo
{
    Task<LiveVideoFormat?> ReadFormatAsync(string segmentPath, CancellationToken cancellationToken = default);
    Task<TimeshiftVideoArguments> EncodeArgumentsAsync(LiveVideoEncode encode, bool onCpu, CancellationToken cancellationToken = default);
}
