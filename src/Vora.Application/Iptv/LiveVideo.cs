using System.Globalization;

namespace Vora.Application.Iptv;

/// <summary>
/// A channel's video as playback saw it. <paramref name="Interlaced"/> is null while only the codec is
/// known, as when an HDHomeRun lineup names a channel's codec and nothing else.
/// </summary>
public sealed record LiveVideoFormat(string Codec, string? Profile, int? Level, int? Height, double? FrameRate, bool? Interlaced);

public sealed record LiveVideoLimits(IReadOnlyList<string>? VideoCodecs, int? MaxH264Level, IReadOnlyList<string>? H264Profiles, int? MaxHeight = null)
{
    /// <summary>
    /// What a player that did not say is assumed to take: the modern codecs, not the legacy ones
    /// (MPEG-2 from an over-the-air tuner, MPEG-4 part 2, VC-1), which no browser or Apple player plays
    /// in HLS. H.264 is not checked for profile or level, so a channel that copied before still copies.
    /// </summary>
    public static LiveVideoLimits Default { get; } = new(["h264", "hevc", "vp9", "av1"], null, null);

    public bool IsEmpty => VideoCodecs is null && MaxH264Level is null && H264Profiles is null;

    /// <summary>The limits the player sent, or <see cref="Default"/> when it sent none.</summary>
    public static LiveVideoLimits Effective(LiveVideoLimits? sent) =>
        sent is null || (sent.IsEmpty && sent.MaxHeight is null) ? Default
        : sent.IsEmpty ? Default with { MaxHeight = sent.MaxHeight }
        : sent;
}

/// <param name="Deinterlace">Whether the encode deinterlaces at all.</param>
/// <param name="OnlyFlaggedFrames">Deinterlace only the frames the stream flags as interlaced, which is what an encode does when it is not known whether the channel is interlaced: a 720p60 channel passes through untouched, a 1080i one is deinterlaced.</param>
public sealed record LiveVideoEncode(int Level, int? MaxHeight, bool Deinterlace, int SegmentSeconds = LiveVideoDecision.TimeshiftSegmentSeconds, bool OnlyFlaggedFrames = false);

public static class LiveVideoDecision
{
    public const string H264 = "h264";
    public const int StandardLevel = 41;
    public const int HighFrameRateLevel = 42;
    public const int MaxEncodedHeight = 1080;
    public static readonly int[] EncodedHeights = [480, 720, MaxEncodedHeight];
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
        var cap = EncodedHeightFor(limits.MaxHeight);
        var tooTall = format.Height is not { } height || height > cap;
        return new LiveVideoEncode(level, tooTall ? cap : null, format.Interlaced != false, segmentSeconds, OnlyFlaggedFrames: format.Interlaced is null);
    }

    public static int EncodedHeightFor(int? playerHeight) =>
        playerHeight is { } height
            ? EncodedHeights.Where(step => step <= height).DefaultIfEmpty(EncodedHeights[0]).Max()
            : MaxEncodedHeight;

    public static LiveVideoFormat? OutputOf(LiveVideoEncode? sessionEncode, LiveVideoFormat? sessionSource) =>
        sessionEncode is null
            ? sessionSource
            : new LiveVideoFormat(H264, "High", sessionEncode.Level, sessionEncode.MaxHeight ?? sessionSource?.Height, sessionSource?.FrameRate, false);

    public static bool Serves(LiveVideoEncode? sessionEncode, LiveVideoFormat? sessionSource, LiveVideoLimits limits)
    {
        var output = OutputOf(sessionEncode, sessionSource);
        if (output is null || !Plays(output, limits)) return false;
        if (sessionEncode is null) return true;

        var wanted = Math.Min(sessionSource?.Height ?? int.MaxValue, EncodedHeightFor(limits.MaxHeight));
        return (output.Height ?? 0) >= wanted;
    }

    private static string LevelName(int level) => (level / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
}
