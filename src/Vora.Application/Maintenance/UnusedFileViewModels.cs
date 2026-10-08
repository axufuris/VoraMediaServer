namespace Vora.Application.Maintenance;

public enum UnusedFileKind
{
    Artwork,
    ProfilePictures,
    ScrubThumbnails,
    DownloadedSubtitles,
    OriginalArtworkCache,
    SubtitleCache,
    RemovedPlugins,
    UnfinishedFiles,
    Recordings
}

public sealed class UnusedFileGroupVM
{
    public UnusedFileKind Kind { get; set; }
    public string Folder { get; set; } = string.Empty;
    public int Files { get; set; }
    public long Bytes { get; set; }
    public bool Removable { get; set; }
    public List<string> Examples { get; set; } = new();
}

public sealed class UnusedFilesReportVM
{
    public DateTime ScannedAt { get; set; }
    public bool Removed { get; set; }
    public int RemovableFiles { get; set; }
    public long RemovableBytes { get; set; }
    public List<UnusedFileGroupVM> Groups { get; set; } = new();
}
