using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming.Matrix;

public sealed record MatrixRule(string Name, Func<MatrixScenario, MatrixOutcome, Func<MatrixScenario, Task<MatrixOutcome>>, Task<bool>> IsBrokenAsync);

public static class MatrixRules
{
    private const StreamingProfile Direct = StreamingProfile.DirectStreamPreference;
    private const StreamingProfile ClientPreference = StreamingProfile.ClientPreference;
    private const StreamingProfile Bitrate = StreamingProfile.BandwidthOptimized;
    private const int KnifeEdge = 10;

    public static readonly MatrixRule[] All =
    [
        Rule("Direct never converts when something plays without converting",
            (s, o) => s.Profile == Direct && Any(s, PlaysWithoutConversion) && o.Strategy == StreamStrategy.Transcode),
        Rule("Direct on Automatic plays the file itself when it can, unless only the server can hand over the surround the output has",
            (s, o) => s.Profile == Direct && s.ChannelPick == 0 && s.PicksTrackAutomatically && o.Strategy != StreamStrategy.DirectPlay
                && Any(s, PlaysDirectly)
                && !(Wanted(s) > 2 && o.HeardChannels > 2 && !Any(s, (version, track, scenario) => PlaysDirectly(version, track, scenario) && track.Channels > 2))),
        new("Direct never converts because of a channel pick", async (s, o, run) =>
        {
            if (s.Profile != Direct || s.ChannelPick == 0 || s.Client.OldApp) return false;
            var automatic = await run(s with { ChannelPick = 0 });
            return automatic.Strategy != StreamStrategy.Transcode && o.Strategy == StreamStrategy.Transcode;
        }),
        Rule("Direct with a pick never plays a track directly when a closer one also plays directly",
            (s, o) => s.Profile == Direct && HasPick(s)
                && Eligible(o.Version, s).Any(track => PlaysDirectly(o.Version, track, s) && Fit(s, track.Channels) < Fit(s, o.HeardChannels))),
        Rule("Direct plays the picked layout when a track in it plays without converting",
            (s, o) => s.Profile == Direct && HasPick(s)
                && Eligible(o.Version, s).Any(track => track.Channels == s.ChannelPick && PlaysWithoutConversion(o.Version, track, s))
                && o.HeardChannels != s.ChannelPick),
        Rule("Direct plays the largest version that plays without converting",
            (s, o) => s.Profile == Direct
                && Versions(s).Any(version => version.Height > o.Version.Height && Eligible(version, s).Any(track => PlaysWithoutConversion(version, track, s)))),
        Rule("Bitrate plays the largest version whose video plays as it is within the bitrate",
            (s, o) => s.Profile == Bitrate
                && Versions(s).Any(version => version.Height > o.Version.Height && VideoPlaysAsIs(version, s) && Eligible(version, s).Any(track => !ReencodesVideo(version, track, s)))),
        Rule("Direct and Bitrate do not convert the video when another version plays with its video untouched",
            (s, o) => s.Profile != ClientPreference && o.VideoConverted
                && Versions(s).Any(version => version != o.Version && VideoPlaysAsIs(version, s) && Eligible(version, s).Any(track => !ReencodesVideo(version, track, s)))),
        Rule("Client Preference and Bitrate honour a pick above what was detected",
            (s, o) => s.Profile != Direct && HasPick(s) && s.ChannelPick > s.Client.Speakers
                && Eligible(o.Version, s).Any(track => track.Channels > s.Client.Speakers && track.Channels <= s.ChannelPick && PlaysWithoutConversion(o.Version, track, s))
                && o.HeardChannels <= s.Client.Speakers),
        Rule("Client Preference and Bitrate never give a TV app more channels than the pick",
            (s, o) => s.Profile != Direct && HasPick(s) && !s.Client.IsBrowser && o.HeardChannels > s.ChannelPick),
        Rule("Client Preference and Bitrate keep surround when the output has it and the file has it, unless that re-encodes the video",
            (s, o) => s.Profile != Direct && s.PicksTrackAutomatically && Wanted(s) > 2 && o.HeardChannels <= 2
                && Eligible(o.Version, s).Any(track => track.Channels > 2 && !ReencodesVideo(o.Version, track, s))),
        Rule("Client Preference and Bitrate use the track in the wanted layout when it plays without converting",
            (s, o) => s.Profile != Direct && s.PicksTrackAutomatically && (s.ChannelPick > 0 || !s.Client.IsBrowser)
                && Eligible(o.Version, s).Any(track => track.Channels == FullestWanted(o.Version, s) && PlaysWithoutConversion(o.Version, track, s))
                && o.HeardChannels != FullestWanted(o.Version, s)),
        Rule("Converted surround is Dolby Digital at 5.1 or less for an app that plays it, and AAC for everything else",
            (s, o) => o.AudioConverted && (o.HeardCodec != ConversionCodec(s, o) || (o.HeardCodec == "ac3" && o.HeardChannels > 6))),
        Rule("The server never downmixes for a browser, which does that itself",
            (s, o) => s.Client.IsBrowser && o.Reason.Contains("Downmix", StringComparison.Ordinal)),
        Rule("The track the server picked is the track heard",
            (s, o) => o.Heard != o.Selected),
        Rule("Automatic stays in the dialogue language and off the commentary",
            (s, o) => s.PicksTrackAutomatically && !Eligible(o.Version, s).Contains(o.Selected)),
        Rule("A track picked by hand plays, except that Direct and Bitrate play one in its language that needs no conversion when it would need one",
            (s, o) => s.ManualTrack != null && !Eligible(o.Version, s).Contains(o.Selected)),
        Rule("A version picked by hand plays, except that Direct and Bitrate play one that needs no conversion when it would need one",
            (s, o) => s.ManualVersion != null && !Versions(s).Contains(o.Version)),
        Rule("Never sends more channels than the limit",
            (s, o) => o.HeardChannels > HardLimit(s)),
        Rule("Never copies audio the client cannot decode or the stream cannot carry",
            (s, o) => !o.AudioConverted && (!s.Client.Audio.Contains(o.Heard.Codec) || (o.Strategy != StreamStrategy.DirectPlay && !CanCopyToHls(o.Heard.Codec)))),
        Rule("Audio is converted only when it has to be",
            (s, o) => o.AudioConverted && !AudioMustConvert(o.Version, o.Selected, s)),
        Rule("Audio is converted only when no other track gives the same kind of sound, stereo or surround, without converting",
            (s, o) => o.AudioConverted && s.PicksTrackAutomatically
                && Eligible(o.Version, s).Any(track => !AudioMustConvert(o.Version, track, s) && (track.Channels > 2) == (o.HeardChannels > 2))),
        Rule("Never exceeds the resolution limit",
            (s, o) => o.OutputHeight > s.MaxHeight),
        Rule("A resolution pick plays a version that fits when one plays without converting",
            (s, o) => s.ResolutionPick > 0 && Any(s, PlaysWithoutConversion) && o.VideoConverted),
        Rule("Never sends more than the bitrate limit",
            (s, o) => s.Situation.LimitKbps > 0 && o.BandwidthKbps > s.Situation.LimitKbps),
        Rule("Never sends HDR or 10-bit video untouched to a client that cannot show it",
            (s, o) => !o.VideoConverted && (!HdrShows(o.Version, s.Client) || !BitDepthShows(o.Version, s.Client))),
        Rule("An image subtitle is burned into the picture and a text subtitle never is",
            (s, o) => s.Situation.BurnsSubtitleIn != o.BurnsSubtitleIn || (o.BurnsSubtitleIn && !o.VideoConverted)),
        Rule("Video is converted only when it cannot play as it is, or the stream has to be HLS and the client's HLS player cannot take the codec",
            (s, o) => o.VideoConverted && VideoPlaysAsIs(o.Version, s) && !ReencodesVideo(o.Version, o.Selected, s)),
        Rule("Never copies video into HLS that the client's HLS player cannot take",
            (s, o) => !o.VideoConverted && o.Strategy != StreamStrategy.DirectPlay && !s.Client.HlsCarries(o.Version.VideoCodec)),
        Rule("Never re-encodes the video for the sake of the stream when another track avoids it",
            (s, o) => o.VideoConverted && VideoPlaysAsIs(o.Version, s) && Eligible(o.Version, s).Any(track => !ReencodesVideo(o.Version, track, s))),
        Rule("No choice between different kinds of outcome is decided by fewer than ten points",
            (_, o) => o.Margin < KnifeEdge),
    ];

    private static MatrixRule Rule(string name, Func<MatrixScenario, MatrixOutcome, bool> isBroken) =>
        new(name, (scenario, outcome, _) => Task.FromResult(isBroken(scenario, outcome)));

    private static bool HasPick(MatrixScenario s) => s.ChannelPick > 0 && s.PicksTrackAutomatically && !s.Client.OldApp;

    private static bool CanCopyToHls(string codec) => codec is "aac" or "mp3" or "ac3" or "eac3";

    private static string ConversionCodec(MatrixScenario s, MatrixOutcome o) =>
        !s.Client.IsBrowser && s.Client.Audio.Contains("ac3") && Math.Min(o.Selected.Channels, Wanted(s)) > 2 ? "ac3" : "aac";

    private static int HardLimit(MatrixScenario s)
    {
        if (s.Client.OldApp) return s.Client.OlderSingleValue(s.ChannelPick);
        if (s.Client.IsBrowser) return 8;
        if (s.ChannelPick == 0) return s.Client.Speakers;
        return s.Profile == Direct ? Math.Max(s.Client.Speakers, s.ChannelPick) : s.ChannelPick;
    }

    private static int Wanted(MatrixScenario s)
    {
        if (s.Client.OldApp) return s.Client.OlderSingleValue(s.ChannelPick);
        return s.ChannelPick > 0 ? s.ChannelPick : s.Client.Speakers;
    }

    private static int FullestWanted(MatrixVersion version, MatrixScenario s) =>
        Math.Min(Wanted(s), Eligible(version, s).Max(track => track.Channels));

    private static int Fit(MatrixScenario s, int channels) =>
        (Wanted(s) > 2 && channels <= 2 ? 100 : 0) + Math.Abs(Wanted(s) - channels);

    private static List<MatrixVersion> Versions(MatrixScenario s)
    {
        if (s.ManualVersion == null) return s.Title.Versions.ToList();

        var picked = s.Title.Versions.Where(version => version.Label == s.ManualVersion).ToList();
        if (s.Profile == ClientPreference || picked.Any(version => VideoPlaysAsPicked(version, s))) return picked;
        var playing = s.Title.Versions.Where(version => VideoPlaysAsPicked(version, s)).ToList();
        return playing.Count > 0 ? playing : picked;
    }

    private static bool VideoPlaysAsPicked(MatrixVersion version, MatrixScenario s) =>
        VideoPlaysAsIs(version, s) && Eligible(version, s).Any(track => !ReencodesVideo(version, track, s));

    private static bool TrackPlaysAsPicked(MatrixVersion version, MatrixTrack track, MatrixScenario s) =>
        !AudioMustConvert(version, track, s) && !ReencodesVideo(version, track, s);

    private static List<MatrixTrack> Eligible(MatrixVersion version, MatrixScenario s)
    {
        if (s.ManualTrack != null)
        {
            var picked = version.Tracks.Where(track => track.Label == s.ManualTrack).ToList();
            if (s.Profile == ClientPreference || picked.Count == 0 || picked.Any(track => TrackPlaysAsPicked(version, track, s))) return picked;
            var inItsLanguage = version.Tracks
                .Where(track => track.Language == picked[0].Language && track.Commentary == picked[0].Commentary && TrackPlaysAsPicked(version, track, s))
                .ToList();
            return inItsLanguage.Count > 0 ? inItsLanguage : picked;
        }

        var tracks = version.Tracks.Where(track => !track.Commentary).ToList();
        if (tracks.Count == 0) tracks = version.Tracks.ToList();
        var sameLanguage = tracks.Where(track => track.Language == version.Default.Language).ToList();
        return sameLanguage.Count > 0 ? sameLanguage : tracks;
    }

    private static bool HdrShows(MatrixVersion version, MatrixClient client)
    {
        if (version.Hdr == null || client.Hdr == null) return true;
        return version.Hdr switch
        {
            "HDR10" => client.Hdr.Contains("HDR10"),
            "DoVi" => client.Hdr.Contains("DolbyVision"),
            _ => true
        };
    }

    private static bool BitDepthShows(MatrixVersion version, MatrixClient client) =>
        client.BitDepth == 0 || version.BitDepth <= client.BitDepth;

    private static bool OverBitrateLimit(MatrixVersion version, MatrixScenario s) =>
        s.Situation.LimitKbps > 0 && version.Kbps > s.Situation.LimitKbps;

    private static bool VideoPlaysAsIs(MatrixVersion version, MatrixScenario s) =>
        s.Client.Video.Contains(version.VideoCodec) && version.Height <= s.MaxHeight
        && !OverBitrateLimit(version, s) && HdrShows(version, s.Client) && BitDepthShows(version, s.Client) && !s.Situation.BurnsSubtitleIn;

    private static bool AudioPlaysAsIs(MatrixTrack track, MatrixScenario s) =>
        s.Client.Audio.Contains(track.Codec) && track.Channels <= HardLimit(s);

    private static bool NeedsServerStream(MatrixVersion version, MatrixTrack track, MatrixScenario s) =>
        !s.Client.Containers.Contains(version.Container)
        || (!s.Client.SwitchesTracks && !(track == version.Default && version.EveryPlayerStartsOnDefault));

    private static bool AudioMustConvert(MatrixVersion version, MatrixTrack track, MatrixScenario s) =>
        !AudioPlaysAsIs(track, s)
        || ((!VideoPlaysAsIs(version, s) || NeedsServerStream(version, track, s)) && !CanCopyToHls(track.Codec));

    private static bool ReencodesVideo(MatrixVersion version, MatrixTrack track, MatrixScenario s) =>
        VideoPlaysAsIs(version, s) && !s.Client.HlsCarries(version.VideoCodec)
        && (!AudioPlaysAsIs(track, s) || NeedsServerStream(version, track, s));

    private static bool PlaysWithoutConversion(MatrixVersion version, MatrixTrack track, MatrixScenario s) =>
        VideoPlaysAsIs(version, s) && !AudioMustConvert(version, track, s) && !ReencodesVideo(version, track, s);

    private static bool PlaysDirectly(MatrixVersion version, MatrixTrack track, MatrixScenario s) =>
        VideoPlaysAsIs(version, s) && AudioPlaysAsIs(track, s) && !NeedsServerStream(version, track, s);

    private static bool Any(MatrixScenario s, Func<MatrixVersion, MatrixTrack, MatrixScenario, bool> test) =>
        Versions(s).Any(version => Eligible(version, s).Any(track => test(version, track, s)));
}
