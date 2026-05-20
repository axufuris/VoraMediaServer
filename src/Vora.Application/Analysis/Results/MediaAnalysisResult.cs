namespace Vora.Application.Analysis.Results;

public class MediaAnalysisResult
{
    public TimeSpan? Duration { get; set; }
    public long? FileSizeBytes { get; set; }
    public long? OverallBitrate { get; set; }

    public TimeSpan? IntroStart { get; set; }
    public TimeSpan? IntroEnd { get; set; }
    public TimeSpan? CreditsStart { get; set; }

    public List<AudioTrackInfo> AudioTracks { get; set; } = new();

    public List<VideoTrackInfo> VideoTracks { get; set; } = new();
    public List<SubtitleTrackInfo> SubtitleTracks { get; set; } = new();
}

public class AudioTrackInfo
{
    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public int? Channels { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
}

public class VideoTrackInfo
{
    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? HdrType { get; set; }
    public int? BitDepth { get; set; }
    public long? Bitrate { get; set; }
    public bool IsDefault { get; set; }
}

public class SubtitleTrackInfo
{
    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
}