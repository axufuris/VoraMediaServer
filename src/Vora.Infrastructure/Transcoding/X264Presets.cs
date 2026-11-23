namespace Vora.Infrastructure.Transcoding;

internal static class X264Presets
{
    public const int Default = 2;

    public static string Name(int preset) => preset switch
    {
        0 => "ultrafast",
        1 => "superfast",
        2 => "veryfast",
        3 => "faster",
        4 => "fast",
        5 => "medium",
        6 => "slow",
        7 => "slower",
        8 => "veryslow",
        _ => "veryfast"
    };
}
