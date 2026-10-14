using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public sealed record AudioOutputRequest(int DetectedChannels, int PickedChannels, bool PlaysAnyLayout, bool CanSwitchTracks);

public sealed record AudioConversion(string Codec, int Channels);

public sealed record AudioOutputLimits(int Hard, int Target, int Pick)
{
    private const int Stereo = 2;
    private const int Widest = 8;

    public static AudioOutputLimits Resolve(int reportedChannels, AudioOutputRequest? request, StreamingProfile profile)
    {
        int reported = reportedChannels > 0 ? reportedChannels : Stereo;
        if (request is not { DetectedChannels: > 0 }) return new AudioOutputLimits(reported, reported, 0);

        int detected = request.DetectedChannels;
        int pick = Math.Max(0, request.PickedChannels);
        int target = pick > 0 ? pick : detected;
        int hard = request.PlaysAnyLayout
            ? Widest
            : profile == StreamingProfile.DirectStreamPreference ? Math.Max(detected, target) : target;

        return new AudioOutputLimits(hard, target, pick);
    }

    public bool NeedsDownmix(int trackChannels) => trackChannels > Hard;

    public AudioConversion ConversionOf(int trackChannels, bool clientPlaysDolbyDigital)
    {
        int channels = Math.Min(trackChannels, Target);
        return channels > Stereo && clientPlaysDolbyDigital
            ? new AudioConversion(AudioPlayback.DolbyDigital, Math.Min(channels, AudioPlayback.DolbyDigitalChannels))
            : new AudioConversion(AudioPlayback.Aac, channels);
    }

    public int FitPenalty(int trackChannels, int heardChannels, string? heardCodec, bool converted)
    {
        int penalty = Math.Abs(Target - heardChannels) * AudioPlayback.LayoutPenaltyPerChannel;
        penalty -= AudioPlayback.QualityRank(heardCodec);
        if (Target > Stereo && heardChannels <= Stereo) penalty += AudioPlayback.SurroundLostPenalty;
        if (Pick > 0 && (converted || trackChannels != Pick)) penalty += AudioPlayback.PickMissedPenalty;
        return penalty;
    }
}

public static class AudioPlayback
{
    public const string Aac = "aac";
    public const string DolbyDigital = "ac3";
    public const int DolbyDigitalChannels = 6;

    public const int RemuxPenalty = 50;
    public const int ConversionPenalty = 40;
    public const int LayoutPenaltyPerChannel = 5;
    public const int PickMissedPenalty = 60;
    public const int SurroundLostPenalty = 200;
    public const int VideoReencodedForAudioPenalty = 1000;

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
