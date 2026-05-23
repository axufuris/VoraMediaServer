namespace Vora.Application.Analysis.Results;

public class MediaAnalysisResult
{
    public TimeSpan? Duration { get; set; }
    public long? FileSizeBytes { get; set; }
    public long? OverallBitrate { get; set; }

    public List<DetectedInterval> SilenceIntervals { get; set; } = new();
    public List<DetectedInterval> BlackIntervals { get; set; } = new();

    public List<AudioTrackInfo> AudioTracks { get; set; } = new();

    public List<VideoTrackInfo> VideoTracks { get; set; } = new();
    public List<SubtitleTrackInfo> SubtitleTracks { get; set; } = new();
}

public class DetectedInterval
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public TimeSpan Duration => End - Start;
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
