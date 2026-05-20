using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Library;

public class MediaLibrary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public LibraryType Type { get; set; }

    public List<string> FolderPaths { get; set; } = new();
    public string? ScannerRegex { get; set; }

    public string MetadataProviderId { get; set; } = "tmdb_metadata";
    public string? ArtworkProviderId { get; set; }
    public string? ThirdPartyRating1ProviderId { get; set; }
    public string? ThirdPartyRating2ProviderId { get; set; }

    public bool EnableRealTimeWatching { get; set; } = true;
    public bool UseLocalAssets { get; set; }
    public bool FindExtras { get; set; } = true;
    public bool OnlyShowTrailers { get; set; }
    public bool EnableVideoPreviewThumbnails { get; set; }

    public CollectionDisplayMode CollectionDisplay { get; set; } = CollectionDisplayMode.ShowCollectionsAndItems;
    public int MinimumCollectionSize { get; set; } = 1;

    public bool EnableCreditsDetection { get; set; }
    public bool EnableVoiceActivityDetection { get; set; }
    public bool EnableIntroDetection { get; set; } = true;

    public EpisodeSortOrder EpisodeSorting { get; set; } = EpisodeSortOrder.OldestFirst;
    public EpisodeOrdering EpisodeOrder { get; set; } = EpisodeOrdering.TheTvdb;
    public SeasonDisplayMode SeasonsDisplay { get; set; } = SeasonDisplayMode.Show;
    public bool UseSeasonTitles { get; set; } = true;

    public virtual ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
}
