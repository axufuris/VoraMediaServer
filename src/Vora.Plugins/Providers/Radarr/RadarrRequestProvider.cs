using System.Net.Http;
using System.Text.Json;
using System.Text;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Radarr;

public class RadarrRequestProvider : IRequestProvider
{
    private readonly HttpClient _httpClient;

    public string Id => "radarr_requester";
    public string Name => "Radarr";
    public string ProviderName => "Radarr";
    public string Version => "1.0.0";
    public string Description => "Sends movie requests directly to Radarr.";
    public bool IsSystemPlugin => true;
    public string Type => "Request";
    public string ExternalConfigurationHint => "Add your Radarr connection under System Settings → Request Servers. This plugin only enables the integration.";

    public string[] SupportedMediaTypes => new[] { "Movie" };

    public RadarrRequestProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    private HttpClient CreateClient(string host, int port, bool useSsl, string urlBase, string apiKey)
    {
        var protocol = useSsl ? "https" : "http";
        var baseUrl = $"{protocol}://{host}:{port}/{urlBase.Trim('/')}";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";

        var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    public async Task<IEnumerable<ProviderOptionDto>> GetQualityProfilesAsync(string host, int port, bool useSsl, string urlBase, string apiKey, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(host, port, useSsl, urlBase, apiKey);
        var response = await client.GetAsync("api/v3/qualityprofile", cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider returned {response.StatusCode} for Quality Profiles");

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var profiles = JsonSerializer.Deserialize<JsonElement>(content);

        return profiles.EnumerateArray().Select(p => new ProviderOptionDto
        {
            Id = p.GetProperty("id").GetInt32().ToString(),
            Name = p.GetProperty("name").GetString() ?? "Unknown"
        });
    }

    public async Task<IEnumerable<ProviderOptionDto>> GetRootFoldersAsync(string host, int port, bool useSsl, string urlBase, string apiKey, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(host, port, useSsl, urlBase, apiKey);
        var response = await client.GetAsync("api/v3/rootfolder", cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider returned {response.StatusCode} for Root Folders");

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var folders = JsonSerializer.Deserialize<JsonElement>(content);

        return folders.EnumerateArray().Select(p => new ProviderOptionDto
        {
            Id = p.GetProperty("path").GetString() ?? "",
            Name = p.GetProperty("path").GetString() ?? ""
        });
    }

    public async Task<bool> SubmitRequestAsync(string tmdbId, string title, string host, int port, bool useSsl, string urlBase, string apiKey, string providerSettingsJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<JsonElement>(providerSettingsJson);

            int qualityProfileId = settings.TryGetProperty("qualityProfileId", out var qp) ? qp.GetInt32() : 1;
            string rootFolderPath = settings.TryGetProperty("rootFolderPath", out var rf) ? rf.GetString() ?? "" : "";
            string minAvailability = settings.TryGetProperty("minimumAvailability", out var ma) ? ma.GetString() ?? "announced" : "announced";
            bool searchOnAdd = settings.TryGetProperty("searchOnAdd", out var search) ? search.GetBoolean() : true;

            var client = CreateClient(host, port, useSsl, urlBase, apiKey);

            var payload = new
            {
                title = title,
                tmdbId = int.Parse(tmdbId),
                qualityProfileId = qualityProfileId,
                rootFolderPath = rootFolderPath,
                minimumAvailability = minAvailability,
                monitored = true,
                addOptions = new
                {
                    searchForMovie = searchOnAdd
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("api/v3/movie", content, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
