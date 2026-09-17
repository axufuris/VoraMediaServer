namespace Vora.Plugins.Dtos;

public class MusicArtworkResult
{
    public required string Url { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public required string ProviderName { get; init; }
}
