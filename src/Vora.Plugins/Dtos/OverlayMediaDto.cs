namespace Vora.Plugins.Dtos;

public class OverlayMediaDto
{
    public Guid Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? Edition { get; set; }
    public string? ContentRating { get; set; }
    public string? Resolution { get; set; }
    public string? VideoFormat { get; set; }
    public string? AudioCodec { get; set; }
    public bool HasStinger { get; set; }

    public decimal? ServerAdminRating { get; set; }
    public string? ThirdPartyRating1Name { get; set; }
    public decimal? ThirdPartyRating1 { get; set; }
    public string? ThirdPartyRating2Name { get; set; }
    public decimal? ThirdPartyRating2 { get; set; }

    public string? PosterUrl { get; set; }
    public string? OriginalPosterUrl { get; set; }
}
