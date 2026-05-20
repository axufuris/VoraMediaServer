namespace Vora.Plugins.Interfaces;

public interface IChronologyProvider : IVoraPlugin
{
    string ProviderId { get; }
    string ProviderName { get; }

    string ExternalIdLabel { get; }
    string ExternalIdPlaceholder { get; }

    Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null);
}

public class ChronologyResult
{
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string MediaType { get; set; } = "Movie";
    public decimal SortOrder { get; set; }
}
