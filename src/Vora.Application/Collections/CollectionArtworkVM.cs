namespace Vora.Application.Collections;

public class CollectionArtworkVM
{
    public string Id { get; set; } = string.Empty;
    public bool IsUserUploaded { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Language { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? VoteAverage { get; set; }
}
