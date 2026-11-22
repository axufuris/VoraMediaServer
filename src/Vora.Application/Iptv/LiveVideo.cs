using System.Globalization;

namespace Vora.Application.Iptv;

public sealed record LiveVideoFormat(string Codec, string? Profile, int? Level, int? Height, double? FrameRate, bool Interlaced);

public sealed record LiveVideoLimits(IReadOnlyList<string>? VideoCodecs, int? MaxH264Level, IReadOnlyList<string>? H264Profiles)
{
    /// <summary>
    /// What a player that did not say is assumed to take: the modern codecs, not the legacy ones
    /// (MPEG-2 from an over-the-air tuner, MPEG-4 part 2, VC-1), which no browser or Apple player plays
    /// in HLS. H.264 is not checked for profile or level, so a channel that copied before still copies.
    /// </summary>
    public static LiveVideoLimits Default { get; } = new(["h264", "hevc", "vp9", "av1"], null, null);

    public bool IsEmpty => VideoCodecs is null && MaxH264Level is null && H264Profiles is null;

    /// <summary>The limits the player sent, or <see cref="Default"/> when it sent none.</summary>
    public static LiveVideoLimits Effective(LiveVideoLimits? sent) => sent is { IsEmpty: false } ? sent : Default;
}

public sealed record LiveVideoEncode(int Level, int? MaxHeight, bool Deinterlace, int SegmentSeconds = LiveVideoDecision.TimeshiftSegmentSeconds);

public static class LiveVideoDecision
{
    public const string H264 = "h264";
    public const int StandardLevel = 41;
    public const int HighFrameRateLevel = 42;
    public const int MaxEncodedHeight = 1080;
    public const int TimeshiftSegmentSeconds = 4;

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

    public static LiveVideoEncode EncodeFor(LiveVideoFormat format, LiveVideoLimits limits, int segmentSeconds = TimeshiftSegmentSeconds)
    {
        var wanted = format.FrameRate > StandardLevelMaxFrameRate ? HighFrameRateLevel : StandardLevel;
        var level = limits.MaxH264Level is { } maxLevel ? Math.Min(wanted, maxLevel) : wanted;
        var tooTall = format.Height is not { } height || height > MaxEncodedHeight;
        return new LiveVideoEncode(level, tooTall ? MaxEncodedHeight : null, format.Interlaced, segmentSeconds);
    }

    private static string LevelName(int level) => (level / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
}
