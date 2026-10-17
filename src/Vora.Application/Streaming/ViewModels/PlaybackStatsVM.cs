namespace Vora.Application.Streaming.ViewModels;

public class PlaybackStatsVM
{
    public required string Method { get; set; }
    public required string Delivery { get; set; }
    public string? SourceContainer { get; set; }
    public int? SourceBitrateKbps { get; set; }
    public int? StreamBitrateKbps { get; set; }
    public PlaybackVideoStatsVM? Video { get; set; }
    public PlaybackAudioStatsVM? Audio { get; set; }
    public PlaybackSubtitleStatsVM? Subtitle { get; set; }
    public PlaybackTranscoderStatsVM? Transcoder { get; set; }
}

public class PlaybackVideoStatsVM
{
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? Level { get; set; }
    public int? BitDepth { get; set; }
    public string? HdrType { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public int? BitrateKbps { get; set; }
    public required string Strategy { get; set; }
    public string? OutputCodec { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public string? OutputHdrType { get; set; }
    public int? OutputBitrateKbps { get; set; }
    public int? OutputPeakKbps { get; set; }
    public string? ToneMapping { get; set; }
}

public class PlaybackAudioStatsVM
{
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    public int? SampleRate { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public int? BitrateKbps { get; set; }
    public required string Strategy { get; set; }
    public string? OutputCodec { get; set; }
    public int? OutputChannels { get; set; }
    public int? OutputBitrateKbps { get; set; }
}

public class PlaybackSubtitleStatsVM
{
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsForced { get; set; }
    public bool IsExternal { get; set; }
    public required string Strategy { get; set; }
}

public class PlaybackTranscoderStatsVM
{
    public string? Encoder { get; set; }
    public string? EncodeHardware { get; set; }
    public string? DecodeHardware { get; set; }
    public double? Fps { get; set; }
    public double? Speed { get; set; }
    public bool Running { get; set; }
    public bool RetriedOnCpu { get; set; }
    public bool Paused { get; set; }
}
