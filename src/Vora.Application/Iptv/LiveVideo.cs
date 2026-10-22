using System.Globalization;

namespace Vora.Application.Iptv;

public sealed record LiveVideoFormat(string Codec, string? Profile, int? Level, int? Height, double? FrameRate, bool Interlaced);

public sealed record LiveVideoLimits(IReadOnlyList<string>? VideoCodecs, int? MaxH264Level, IReadOnlyList<string>? H264Profiles)
{
    public bool IsEmpty => VideoCodecs is null && MaxH264Level is null && H264Profiles is null;
}

public sealed record LiveVideoEncode(int Level, int? MaxHeight, bool Deinterlace);

public static class LiveVideoDecision
{
    public const string H264 = "h264";
    public const int StandardLevel = 41;
    public const int HighFrameRateLevel = 42;
    public const int MaxEncodedHeight = 1080;

    private const double StandardLevelMaxFrameRate = 30.5;

    public static string? Mismatch(LiveVideoFormat format, LiveVideoLimits limits)
    {
        if (limits.VideoCodecs is { } codecs && !codecs.Any(codec => string.Equals(codec, format.Codec, StringComparison.OrdinalIgnoreCase)))
        {
            return $"the player doesn't take {format.Codec}";
        }

        if (!string.Equals(format.Codec, H264, StringComparison.OrdinalIgnoreCase)) return null;

        if (limits.H264Profiles is { } profiles && format.Profile is { } profile
            && !profiles.Any(allowed => NormaliseProfile(allowed) == NormaliseProfile(profile)))
        {
            return $"the player doesn't take H.264 {profile}";
        }

        if (limits.MaxH264Level is { } maxLevel && format.Level is { } level && level > maxLevel)
        {
            return $"H.264 level {LevelName(level)} is above the player's {LevelName(maxLevel)}";
        }

        return null;
    }

    public static bool Plays(LiveVideoFormat format, LiveVideoLimits limits) => Mismatch(format, limits) is null;

    public static string NormaliseProfile(string profile)
    {
        var name = profile.Trim().ToLowerInvariant();
        return name == "constrained baseline" ? "baseline" : name;
    }

    public static LiveVideoEncode EncodeFor(LiveVideoFormat format, LiveVideoLimits limits)
    {
        var wanted = format.FrameRate > StandardLevelMaxFrameRate ? HighFrameRateLevel : StandardLevel;
        var level = limits.MaxH264Level is { } maxLevel ? Math.Min(wanted, maxLevel) : wanted;
        var tooTall = format.Height is not { } height || height > MaxEncodedHeight;
        return new LiveVideoEncode(level, tooTall ? MaxEncodedHeight : null, format.Interlaced);
    }

    private static string LevelName(int level) => (level / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
}
