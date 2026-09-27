namespace Vora.Application.Libraries.Requests;

public class UpdateLibraryRequest
{
    public string Name { get; set; } = string.Empty;
    public List<string> FolderPaths { get; set; } = new();
    public string? ScannerRegex { get; set; }
    public List<string> ExcludeFilters { get; set; } = new();

    public bool EnableRealTimeWatching { get; set; }
    public bool FindExtras { get; set; }
    public bool OnlyShowTrailers { get; set; }
    public bool EnableVideoPreviewThumbnails { get; set; }
    public bool EnableCreditsDetection { get; set; }
    public bool EnablePreviewDetection { get; set; }
    public int MinimumCollectionSize { get; set; }

    public string MetadataProviderId { get; set; } = string.Empty;

    public int EpisodeSorting { get; set; }
    public int EpisodeOrder { get; set; }
    public bool UseSeasonTitles { get; set; }
    public int SeasonsDisplay { get; set; }
    public bool EnableIntroDetection { get; set; }
    public string? ThirdPartyRating1ProviderId { get; set; }
    public string? ThirdPartyRating2ProviderId { get; set; }
    public string? ArtworkProviderId { get; set; }
}