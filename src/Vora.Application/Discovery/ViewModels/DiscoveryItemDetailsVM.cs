using Vora.Plugins.Dtos;

namespace Vora.Application.Discovery.ViewModels;

public class DiscoveryCastMemberVM
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
}

public class DiscoveryTrailerVM
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class DiscoveryItemDetailsVM
{
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Year { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? Overview { get; set; }
    public string? ContentRating { get; set; }
    public DateTime? NextAirDate { get; set; }
    public int? RuntimeMinutes { get; set; }
    public decimal? Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Studios { get; set; } = new();
    public List<DiscoveryCastMemberVM> Cast { get; set; } = new();
    public List<DiscoveryTrailerVM> Trailers { get; set; } = new();
    public bool InLibrary { get; set; }

    public static DiscoveryItemDetailsVM FromDto(DiscoveryItemDetailsDto dto, bool inLibrary) => new()
    {
        ExternalId = dto.ExternalId,
        ProviderId = dto.ProviderId,
        Title = dto.Title,
        Type = dto.Type,
        Year = dto.Year,
        ReleaseDate = dto.ReleaseDate,
        PosterUrl = dto.PosterUrl,
        BackgroundUrl = dto.BackgroundUrl,
        Overview = dto.Overview,
        ContentRating = dto.ContentRating,
        NextAirDate = dto.NextAirDate,
        RuntimeMinutes = dto.RuntimeMinutes,
        Rating = dto.Rating,
        Genres = dto.Genres,
        Studios = dto.Studios,
        InLibrary = inLibrary,
        Cast = dto.Cast.Select(c => new DiscoveryCastMemberVM
        {
            ExternalId = c.ExternalId,
            Name = c.Name,
            Role = c.Role,
            ProfileImageUrl = c.ProfileImageUrl
        }).ToList(),
        Trailers = dto.Trailers.Select(t => new DiscoveryTrailerVM
        {
            Name = t.Name,
            Url = t.Url
        }).ToList()
    };
}
