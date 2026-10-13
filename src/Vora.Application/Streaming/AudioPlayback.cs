namespace Vora.Application.Streaming;

public static class AudioPlayback
{
    public static bool CanCopyToHls(string? codec) =>
        codec?.ToLowerInvariant() is "aac" or "mp3" or "ac3" or "eac3";

    public static int QualityRank(string? codec) => codec?.ToLowerInvariant() switch
    {
        "truehd" => 6,
        "flac" or "dts" => 5,
        "eac3" => 4,
        "ac3" => 3,
        "aac" or "opus" => 2,
        "mp3" => 1,
        _ => 0
    };
}
