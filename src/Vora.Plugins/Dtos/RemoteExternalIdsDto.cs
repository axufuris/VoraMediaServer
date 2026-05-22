namespace Vora.Plugins.Dtos;

public class RemoteExternalIdsDto
{
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }

    public bool HasAny => !string.IsNullOrEmpty(ImdbId)
        || !string.IsNullOrEmpty(TmdbId)
        || !string.IsNullOrEmpty(TvdbId);
}
