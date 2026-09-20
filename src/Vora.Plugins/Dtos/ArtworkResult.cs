
namespace Vora.Plugins.Dtos;

public class ArtworkResult
{
    public string Id { get; set; } = string.Empty;
    public bool IsUserUploaded { get; set; }
    public string Url { get; set; } = string.Empty;
    public ArtworkKind Kind { get; set; }
    public string? Language { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? VoteAverage { get; set; }
}
