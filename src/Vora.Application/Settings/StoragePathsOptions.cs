namespace Vora.Application.Settings;

public class StoragePathsOptions
{
    public const string SectionName = "StoragePaths";

    public string? CustomArtwork { get; set; }
    public string? OriginalArtworkCache { get; set; }
    public string? UserImages { get; set; }
    public string? Plugins { get; set; }
    public string? VideoThumbnails { get; set; }

    // Downloaded subtitle files. Server-managed and persistent: the media library
    // is read-only, so nothing Vora fetches is ever written beside the video.
    public string? Subtitles { get; set; }
    public string? Logs { get; set; }
    public string? Backups { get; set; }
    public string? DataProtection { get; set; }
    public string? EpgCache { get; set; }
    public string? IptvDvr { get; set; }
    public string? Metadata { get; set; }
    public string? AudioFingerprints { get; set; }
}
