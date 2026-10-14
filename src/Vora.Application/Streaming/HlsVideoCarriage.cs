namespace Vora.Application.Streaming;

public static class HlsVideoCarriage
{
    public const int ReencodePenalty = 1000;

    private static readonly string[] BrowserDefault = ["h264"];
    private static readonly string[] AppDefault = ["h264", "hevc", "mpeg2video"];

    public static IReadOnlySet<string> For(string? deviceType, IReadOnlyCollection<string>? reported)
    {
        var codecs = reported is { Count: > 0 } ? reported : deviceType == "Browser" ? BrowserDefault : AppDefault;
        return codecs.Select(codec => codec.ToLowerInvariant()).ToHashSet();
    }
}
