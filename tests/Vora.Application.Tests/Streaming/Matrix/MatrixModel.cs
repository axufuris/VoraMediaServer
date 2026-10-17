using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming.Matrix;

public sealed record MatrixClient(string Name, string DeviceType, string[] Video, string[] Audio, string[] Containers, int Speakers, int MaxHeight, string[]? Hdr, int BitDepth, bool SwitchesTracks = false, bool OldApp = false, string[]? HlsVideo = null, int Display = 0)
{
    public bool IsBrowser => DeviceType == "Browser";

    public bool HlsCarries(string videoCodec) => (HlsVideo ?? (IsBrowser ? ["h264"] : ["h264", "hevc", "mpeg2video"])).Contains(videoCodec);

    public int OlderSingleValue(int pick) => pick > 0 ? Math.Min(pick, Speakers) : Speakers;

    public bool ShowsHdr(string format) => Hdr?.Contains(format) == true;
}

public sealed record MatrixTrack(string Label, string Codec, int Channels, bool IsDefault = false, string Language = "eng", bool Commentary = false);

public sealed record MatrixVersion(string Label, string VideoCodec, int Height, string Container, int Kbps, MatrixTrack[] Tracks, string? Hdr = null, int BitDepth = 8)
{
    public MatrixTrack Default => Tracks.FirstOrDefault(track => track.IsDefault) ?? Tracks[0];

    public MatrixTrack First => Tracks[0];

    public bool EveryPlayerStartsOnDefault => Default == First;

    public MatrixTrack PlainPlayerStartsOn(MatrixClient client) => client.IsBrowser && Container == "mkv" ? First : Default;
}

public sealed record MatrixTitle(string Label, MatrixVersion[] Versions);

public sealed record MatrixSituation(string Label, int LimitKbps = 0, string LimitSource = "None", string Subtitle = MatrixSituation.NoSubtitle, bool HdrDownscaleAlways = false, bool GpuKeepsHdr = false)
{
    public const string NoSubtitle = "off";
    public const string TextSubtitle = "text";
    public const string ImageSubtitle = "image";
    public const string ForcedImageSubtitle = "forced";

    public bool BurnsSubtitleIn => Subtitle is ImageSubtitle or ForcedImageSubtitle;
}

public sealed record MatrixScenario(MatrixTitle Title, MatrixClient Client, StreamingProfile Profile, int ChannelPick, int ResolutionPick, string? ManualTrack, MatrixSituation Situation, string? ManualVersion = null, string? PreferredLanguage = null)
{
    public string Key => $"{Profile} | {Client.Name} | {Title.Label} | channels {(ChannelPick == 0 ? "Automatic" : ChannelPick)} | resolution {(ResolutionPick == 0 ? "Automatic" : ResolutionPick)} | track {ManualTrack ?? "Automatic"} | version {ManualVersion ?? "Automatic"} | {Situation.Label}"
        + (PreferredLanguage == null ? "" : $" | profile audio language {PreferredLanguage}");

    public int MaxHeight => ResolutionPick > 0 ? Math.Min(ResolutionPick, Client.MaxHeight) : Client.MaxHeight;

    public bool PicksTrackAutomatically => ManualTrack == null;
}

public sealed record MatrixOutcome(StreamStrategy Strategy, string VideoStrategy, string AudioStrategy, MatrixVersion Version, MatrixTrack Selected, MatrixTrack Heard, int HeardChannels, string HeardCodec, int OutputHeight, string Reason, int Margin, int BandwidthKbps, bool BurnsSubtitleIn, string OutputHdr = "SDR")
{
    public bool AudioConverted => AudioStrategy == "Transcode";

    public bool VideoConverted => VideoStrategy == "Transcode";

    public string Summary => $"{Strategy} of the {Version.Label} version, video {VideoStrategy}, audio {AudioStrategy}, hears '{Heard.Label}' as {HeardChannels} channels" + (Heard == Selected ? "" : $" although the server picked '{Selected.Label}'");
}
