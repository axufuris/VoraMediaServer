namespace Vora.Plugins.Dtos;

public class MetadataResult
{
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? ContentRating { get; set; }

    public bool IsAdult { get; set; }
    public string? OriginalTitle { get; set; }
    public string? OriginalLanguage { get; set; }
    public decimal? Rating { get; set; }

    public string? Status { get; set; }
    public string? Tagline { get; set; }
    public string? HomePage { get; set; }
    public long? Budget { get; set; }
    public long? Revenue { get; set; }
    public int? RuntimeMinutes { get; set; }

    public bool? InProduction { get; set; }
    public string? TvType { get; set; }
    public int? NumberOfEpisodes { get; set; }
    public int? NumberOfSeasons { get; set; }
    public DateTime? LastAirDate { get; set; }
    public DateTime? NextAirDate { get; set; }
    public string? LastEpisodeToAirName { get; set; }
    public string? NextEpisodeToAirName { get; set; }

    public bool HasMidCreditsStinger { get; set; } = false;
    public bool HasPostCreditsStinger { get; set; } = false;

    public CollectionResult? Collection { get; set; }
    public List<int> GenreIds { get; set; } = new List<int>();
    public List<UpcomingEpisodeResult> UpcomingEpisodes { get; set; } = new List<UpcomingEpisodeResult>();
    public List<CastMemberResult> Cast { get; set; } = new List<CastMemberResult>();
    public List<CompanyResult> ProductionCompanies { get; set; } = new List<CompanyResult>();
    public List<CountryResult> OriginCountries { get; set; } = new List<CountryResult>();
    public List<NetworkResult> Networks { get; set; } = new List<NetworkResult>();
    public List<SeasonResult> Seasons { get; set; } = new List<SeasonResult>();
    public List<VideoResult> Videos { get; set; } = new List<VideoResult>();
}

public class ActorMetadataResult
{
    public string? Biography { get; set; }
    public DateTime? Birthday { get; set; }
    public DateTime? Deathday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? ImdbId { get; set; }
    public string? HomePage { get; set; }
}

public class CollectionResult
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
}

public class CastMemberResult
{
    public int TmdbId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CharacterName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public CastRole Roles { get; set; } = CastRole.Actor;
}

public class CompanyResult
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public string? OriginCountry { get; set; }
}

public class CountryResult
{
    public string IsoCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class NetworkResult
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public string? OriginCountry { get; set; }
}

public class SeasonResult
{
    public int Id { get; set; }
    public int SeasonNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public DateTime? AirDate { get; set; }
    public int EpisodeCount { get; set; }
    public decimal? VoteAverage { get; set; }
    public List<UpcomingEpisodeResult> UpcomingEpisodes { get; set; } = new List<UpcomingEpisodeResult>();
}

public class VideoResult
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsOfficial { get; set; }
}

public class UpcomingEpisodeResult
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AirDate { get; set; }
}
