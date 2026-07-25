using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Common;
using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Media;

public abstract class MediaItem : LockableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Title { get; set; }
    public string? SortTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public string? OriginalLanguage { get; set; }

    public string? Overview { get; set; }
    public string? Tagline { get; set; }
    public string? Edition { get; set; }
    public string? Status { get; set; }
    public string? HomePage { get; set; }
    public string? ContentRating { get; set; }
    public bool IsAdult { get; set; }

    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }

    public decimal? ServerAdminRating { get; set; }
    public decimal? ThirdPartyRating1 { get; set; }
    public string? ThirdPartyRating1Name { get; set; }
    public decimal? ThirdPartyRating2 { get; set; }
    public string? ThirdPartyRating2Name { get; set; }

    public string? PosterUrl { get; set; }
    public string? OriginalPosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }

    public DateTime? ReleaseDate { get; set; }
    public bool HasMidCreditsStinger { get; set; }
    public bool HasPostCreditsStinger { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastMetadataRefresh { get; set; }
    public DateTime? LastOverlayGeneratedAt { get; set; }

    public DateTime? LastVideoThumbnailGenerationAt { get; set; }
    public string? VideoThumbnailSpriteVersion { get; set; }
    public int VideoThumbnailSpriteCount { get; set; }
    public int VideoThumbnailIntervalSeconds { get; set; }
    public int VideoThumbnailSpriteColumns { get; set; }
    public int VideoThumbnailWidth { get; set; }
    public int VideoThumbnailHeight { get; set; }

    public Guid LibraryId { get; set; }
    public virtual MediaLibrary Library { get; set; } = null!;

    public virtual MediaItemAnalysis Analysis { get; set; } = null!;

    public virtual ICollection<MediaItemMarker> Markers { get; set; } = new List<MediaItemMarker>();

    public virtual ICollection<MediaPart> MediaParts { get; set; } = new List<MediaPart>();
    public virtual ICollection<MediaArtwork> Artwork { get; set; } = new List<MediaArtwork>();
    public virtual ICollection<MediaVideo> Videos { get; set; } = new List<MediaVideo>();
    public virtual ICollection<MediaExtra> Extras { get; set; } = new List<MediaExtra>();
    public virtual ICollection<MediaCastMember> Cast { get; set; } = new List<MediaCastMember>();
    public virtual ICollection<Genre> Genres { get; set; } = new List<Genre>();
    public virtual ICollection<Company> ProductionCompanies { get; set; } = new List<Company>();
    public virtual ICollection<Country> OriginCountries { get; set; } = new List<Country>();
    public virtual ICollection<Collection> Collections { get; set; } = new List<Collection>();
}
