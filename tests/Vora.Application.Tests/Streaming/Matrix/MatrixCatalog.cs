using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming.Matrix;

public static class MatrixCatalog
{
    private static readonly string[] AllVideo = ["h264", "hevc", "vp9", "av1"];
    private static readonly string[] TvAudio = ["aac", "ac3", "eac3", "opus", "flac", "mp3"];
    private static readonly string[] EdgeAudio = ["aac", "ac3", "eac3", "opus", "flac", "mp3"];
    private static readonly string[] ChromeAudio = ["aac", "opus", "flac", "mp3"];
    private static readonly string[] TvContainers = ["mp4", "webm", "mkv", "hls"];
    private static readonly string[] ChromiumContainers = ["mp4", "webm", "mkv"];
    private static readonly string[] NoHdr = [];
    private static readonly string[] Hdr10 = ["HDR10", "HLG"];
    private static readonly string[] DolbyVision = ["HDR10", "HLG", "DolbyVision"];

    public static readonly MatrixClient[] Clients =
    [
        new("Chrome with stereo speakers", "Browser", AllVideo, ChromeAudio, ChromiumContainers, 2, 2160, NoHdr, 10),
        new("Chrome on a 5.1 PC", "Browser", AllVideo, ChromeAudio, ChromiumContainers, 6, 2160, NoHdr, 10),
        new("Edge with stereo speakers", "Browser", AllVideo, EdgeAudio, ChromiumContainers, 2, 2160, NoHdr, 10),
        new("Edge on a 5.1 PC", "Browser", AllVideo, EdgeAudio, ChromiumContainers, 6, 2160, NoHdr, 10),
        new("Edge on an HDR display", "Browser", AllVideo, EdgeAudio, ChromiumContainers, 2, 2160, Hdr10, 10),
        new("Firefox with stereo speakers", "Browser", ["h264", "vp9", "av1"], ChromeAudio, ["mp4", "webm"], 2, 2160, NoHdr, 10),
        new("Safari with stereo speakers", "Browser", ["h264", "hevc"], ["aac", "ac3", "eac3", "flac", "mp3"], ["mp4", "hls"], 2, 2160, NoHdr, 10),
        new("Android TV with a 7.1 receiver", "AndroidTV", AllVideo, ["aac", "ac3", "eac3", "dts", "truehd", "opus", "flac", "mp3"], TvContainers, 8, 2160, DolbyVision, 10, SwitchesTracks: true),
        new("Android TV with a 5.1 soundbar", "AndroidTV", AllVideo, TvAudio, TvContainers, 6, 2160, Hdr10, 10, SwitchesTracks: true),
        new("Android TV on TV speakers", "AndroidTV", AllVideo, TvAudio, TvContainers, 2, 2160, DolbyVision, 10, SwitchesTracks: true),
        new("Fire TV 1080p on TV speakers", "AndroidTV", ["h264", "hevc", "vp9"], TvAudio, TvContainers, 2, 1080, NoHdr, 8, SwitchesTracks: true),
        new("Android phone", "Android", AllVideo, ChromeAudio, TvContainers, 2, 1080, Hdr10, 10, SwitchesTracks: true),
        new("Roku with a 5.1 receiver", "Roku", ["h264", "hevc", "vp9"], ["aac", "ac3", "eac3", "dts", "flac", "mp3"], ["mp4", "mkv", "hls"], 6, 2160, Hdr10, 10, SwitchesTracks: true),
        new("Roku on TV speakers", "Roku", ["h264", "hevc", "vp9"], ["aac", "ac3", "eac3", "mp3"], ["mp4", "mkv", "hls"], 2, 2160, DolbyVision, 10, SwitchesTracks: true),
        new("Older Android TV app with a 5.1 soundbar", "AndroidTV", AllVideo, TvAudio, TvContainers, 6, 2160, Hdr10, 10, OldApp: true),
    ];

    private static MatrixTrack Track(string label, string codec, int channels, bool isDefault = false, string language = "eng", bool commentary = false) =>
        new(label, codec, channels, isDefault, language, commentary);

    private static readonly (string Label, MatrixTrack[] Tracks)[] AudioSets =
    [
        ("AAC 2.0", [Track("AAC 2.0", "aac", 2, true)]),
        ("AAC 5.1", [Track("AAC 5.1", "aac", 6, true)]),
        ("EAC3 5.1", [Track("EAC3 5.1", "eac3", 6, true)]),
        ("AAC 5.1 + AAC 2.0", [Track("AAC 5.1", "aac", 6, true), Track("AAC 2.0", "aac", 2)]),
        ("AC3 5.1 + AAC 2.0", [Track("AC3 5.1", "ac3", 6, true), Track("AAC 2.0", "aac", 2)]),
        ("AAC 2.0 + EAC3 5.1", [Track("AAC 2.0", "aac", 2, true), Track("EAC3 5.1", "eac3", 6)]),
        ("TrueHD 7.1 + AC3 5.1", [Track("TrueHD 7.1", "truehd", 8, true), Track("AC3 5.1", "ac3", 6)]),
        ("TrueHD 7.1", [Track("TrueHD 7.1", "truehd", 8, true)]),
        ("TrueHD 7.1 + AAC 2.0", [Track("TrueHD 7.1", "truehd", 8, true), Track("AAC 2.0", "aac", 2)]),
        ("DTS 5.1", [Track("DTS 5.1", "dts", 6, true)]),
        ("DTS 5.1 + AC3 5.1 + AAC 2.0", [Track("DTS 5.1", "dts", 6, true), Track("AC3 5.1", "ac3", 6), Track("AAC 2.0", "aac", 2)]),
        ("DTS-HD 7.1 + AC3 5.1", [Track("DTS-HD 7.1", "dts", 8, true), Track("AC3 5.1", "ac3", 6)]),
        ("FLAC 2.0", [Track("FLAC 2.0", "flac", 2, true)]),
        ("FLAC 5.1", [Track("FLAC 5.1", "flac", 6, true)]),
        ("Opus 5.1", [Track("Opus 5.1", "opus", 6, true)]),
        ("EAC3 7.1 + AAC 2.0", [Track("EAC3 7.1", "eac3", 8, true), Track("AAC 2.0", "aac", 2)]),
        ("DTS 5.1 English + AAC 2.0 French", [Track("DTS 5.1 English", "dts", 6, true), Track("AAC 2.0 French", "aac", 2, language: "fra")]),
        ("AAC 2.0 first + EAC3 5.1 flagged default", [Track("AAC 2.0", "aac", 2), Track("EAC3 5.1", "eac3", 6, true)]),
        ("Japanese AAC 2.0 first + English AC3 5.1 flagged default", [Track("AAC 2.0 Japanese", "aac", 2, language: "jpn"), Track("AC3 5.1 English", "ac3", 6, true)]),
        ("EAC3 5.1 + AAC 2.0 commentary", [Track("EAC3 5.1", "eac3", 6, true), Track("AAC 2.0 commentary", "aac", 2, commentary: true)]),
    ];

    private static readonly (string Name, string Codec, int Channels)[] Palette =
    [
        ("AAC 2.0", "aac", 2), ("AAC 5.1", "aac", 6), ("AC3 2.0", "ac3", 2), ("AC3 5.1", "ac3", 6), ("EAC3 5.1", "eac3", 6), ("EAC3 7.1", "eac3", 8),
        ("DTS 5.1", "dts", 6), ("DTS-HD 7.1", "dts", 8), ("TrueHD 7.1", "truehd", 8), ("FLAC 2.0", "flac", 2), ("FLAC 5.1", "flac", 6),
        ("Opus 2.0", "opus", 2), ("Opus 5.1", "opus", 6), ("MP3 2.0", "mp3", 2),
    ];

    private static MatrixTrack FromPalette(int index, bool isDefault = false) =>
        Track(Palette[index].Name, Palette[index].Codec, Palette[index].Channels, isDefault);

    private static IEnumerable<(string Label, MatrixTrack[] Tracks)> EveryCombination()
    {
        for (int first = 0; first < Palette.Length; first++)
        {
            yield return (Palette[first].Name, [FromPalette(first, true)]);
            for (int second = 0; second < Palette.Length; second++)
            {
                if (first == second) continue;
                for (int flagged = 0; flagged < 2; flagged++)
                {
                    yield return ($"{Palette[first].Name} + {Palette[second].Name} with the default flag on track {flagged + 1}",
                        [FromPalette(first, flagged == 0), FromPalette(second, flagged == 1)]);
                }
                for (int third = second + 1; third < Palette.Length; third++)
                {
                    if (third == first) continue;
                    yield return ($"{Palette[first].Name} + {Palette[second].Name} + {Palette[third].Name}",
                        [FromPalette(first, true), FromPalette(second), FromPalette(third)]);
                }
            }
        }
    }

    private static IEnumerable<MatrixTitle> Titles(bool exhaustive)
    {
        foreach (var (label, tracks) in exhaustive ? AudioSets.Concat(EveryCombination()) : AudioSets)
        {
            yield return new($"1080p H.264 MKV with {label}", [new("1080p", "h264", 1080, "mkv", 8000, tracks)]);
            yield return new($"1080p H.264 MP4 with {label}", [new("1080p", "h264", 1080, "mp4", 8000, tracks)]);
            yield return new($"1080p H.264 AVI with {label}", [new("1080p", "h264", 1080, "avi", 8000, tracks)]);
            yield return new($"1080p HEVC MKV with {label}", [new("1080p", "hevc", 1080, "mkv", 6000, tracks)]);
            yield return new($"1080p HEVC 10-bit MKV with {label}", [new("1080p", "hevc", 1080, "mkv", 6000, tracks, BitDepth: 10)]);
            yield return new($"4K HEVC MKV with {label}", [new("4K", "hevc", 2160, "mkv", 50000, tracks)]);
            yield return new($"4K HEVC HDR10 MKV with {label}", [new("4K", "hevc", 2160, "mkv", 60000, tracks, "HDR10", 10)]);
            yield return new($"4K HEVC Dolby Vision MKV with {label}", [new("4K", "hevc", 2160, "mkv", 60000, tracks, "DoVi", 10)]);
            yield return new($"4K HEVC and 1080p H.264 versions with {label}", [new("4K", "hevc", 2160, "mkv", 50000, tracks), new("1080p", "h264", 1080, "mkv", 8000, tracks)]);
            yield return new($"4K HDR10 and 1080p SDR H.264 versions with {label}", [new("4K", "hevc", 2160, "mkv", 60000, tracks, "HDR10", 10), new("1080p", "h264", 1080, "mkv", 8000, tracks)]);
        }
    }

    private static readonly MatrixSituation Plain = new("no limit and no subtitle");

    private static readonly MatrixSituation[] Situations =
    [
        new("20 Mbps bitrate pick", 20000, "Client Limit"),
        new("4 Mbps remote limit", 4000, "Server Limit"),
        new("image subtitle chosen", Subtitle: MatrixSituation.ImageSubtitle),
        new("text subtitle chosen", Subtitle: MatrixSituation.TextSubtitle),
        new("forced image subtitle on Automatic", Subtitle: MatrixSituation.ForcedImageSubtitle),
        new("HDR downscale set to Always", HdrDownscaleAlways: true),
    ];

    private static readonly StreamingProfile[] Profiles =
        [StreamingProfile.DirectStreamPreference, StreamingProfile.ClientPreference, StreamingProfile.BandwidthOptimized];

    private static readonly int[] ChannelPicks = [0, 2, 6, 8];

    public static IEnumerable<MatrixScenario> Scenarios(bool exhaustive)
    {
        foreach (var title in Titles(exhaustive))
        foreach (var client in Clients)
        foreach (var profile in Profiles)
        {
            foreach (var pick in ChannelPicks) yield return new(title, client, profile, pick, 0, null, Plain);

            if (title.Versions[0].Height > 1080) yield return new(title, client, profile, 0, 1080, null, Plain);
            yield return new(title, client, profile, 0, 720, null, Plain);
            yield return new(title, client, profile, 2, 720, null, Plain);

            var tracks = title.Versions[0].Tracks;
            if (tracks.Length > 1 && title.Versions.Length == 1) yield return new(title, client, profile, 0, 0, tracks[^1].Label, Plain);

            foreach (var situation in Situations)
            {
                if (situation.HdrDownscaleAlways && title.Versions.All(version => version.Hdr == null)) continue;
                yield return new(title, client, profile, 0, 0, null, situation);
                if (exhaustive) continue;
                yield return new(title, client, profile, 2, 0, null, situation);
                yield return new(title, client, profile, 6, 0, null, situation);
            }
        }
    }
}
