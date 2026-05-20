using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public class ProviderOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public interface IRequestProvider : IVoraPlugin
{
    string[] SupportedMediaTypes { get; }

    Task<IEnumerable<ProviderOptionDto>> GetQualityProfilesAsync(string host, int port, bool useSsl, string urlBase, string apiKey);
    Task<IEnumerable<ProviderOptionDto>> GetRootFoldersAsync(string host, int port, bool useSsl, string urlBase, string apiKey);

    Task<bool> SubmitRequestAsync(
        string tmdbId,
        string title,
        string host,
        int port,
        bool useSsl,
        string urlBase,
        string apiKey,
        string providerSettingsJson);
}
