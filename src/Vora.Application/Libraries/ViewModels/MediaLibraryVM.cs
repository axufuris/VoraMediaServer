using System.Linq.Expressions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Libraries.ViewModels;

public class MediaLibraryVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> FolderPaths { get; set; } = new();
    public string? ScannerRegex { get; set; }
    public bool EnableRealTimeWatching { get; set; }
    public bool IsBeingWatched { get; set; }
    public int MinimumCollectionSize { get; set; }

    public string MetadataProviderId { get; set; } = string.Empty;
    public bool UseLocalAssets { get; set; }
    public bool FindExtras { get; set; }
    public bool OnlyShowTrailers { get; set; }
    public bool EnableVideoPreviewThumbnails { get; set; }
    public int CollectionDisplay { get; set; }
    public bool EnableCreditsDetection { get; set; }
    public bool EnableVoiceActivityDetection { get; set; }
    public string? ThirdPartyRating1ProviderId { get; set; }
    public string? ThirdPartyRating2ProviderId { get; set; }
    public string? ArtworkProviderId { get; set; }

    public int EpisodeSorting { get; set; }
    public int EpisodeOrder { get; set; }
    public bool UseSeasonTitles { get; set; }
    public int SeasonsDisplay { get; set; }
    public bool EnableIntroDetection { get; set; }

    public static Expression<Func<MediaLibrary, MediaLibraryVM>> Projection =>
        l => new MediaLibraryVM
        {
            Id = l.Id,
            Name = l.Name,
            Type = l.Type == LibraryType.Movie ? "Movie"
                : l.Type == LibraryType.TvShow ? "TvShow"
                : l.Type == LibraryType.Music ? "Music"
                : l.Type == LibraryType.HomeVideo ? "HomeVideo"
                : l.Type == LibraryType.LiveTv ? "LiveTv"
                : "Unknown",
            FolderPaths = l.FolderPaths,
            ScannerRegex = l.ScannerRegex,
            MetadataProviderId = l.MetadataProviderId,
            UseLocalAssets = l.UseLocalAssets,
            FindExtras = l.FindExtras,
            OnlyShowTrailers = l.OnlyShowTrailers,
            EnableVideoPreviewThumbnails = l.EnableVideoPreviewThumbnails,
            CollectionDisplay = (int)l.CollectionDisplay,
            EnableCreditsDetection = l.EnableCreditsDetection,
            EnableVoiceActivityDetection = l.EnableVoiceActivityDetection,
            EpisodeSorting = (int)l.EpisodeSorting,
            EpisodeOrder = (int)l.EpisodeOrder,
            UseSeasonTitles = l.UseSeasonTitles,
            SeasonsDisplay = (int)l.SeasonsDisplay,
            EnableIntroDetection = l.EnableIntroDetection,
            MinimumCollectionSize = l.MinimumCollectionSize,
            ThirdPartyRating1ProviderId = l.ThirdPartyRating1ProviderId,
            ThirdPartyRating2ProviderId = l.ThirdPartyRating2ProviderId,
            ArtworkProviderId = l.ArtworkProviderId,
            EnableRealTimeWatching = l.EnableRealTimeWatching
        };
}
