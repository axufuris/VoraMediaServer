using System.Net.Http;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Radarr;

public class RadarrRequestProvider : IRequestProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RadarrRequestProvider> _logger;

    public string Id => "radarr_requester";
    public string Name => "Radarr";
    public string ProviderName => "Radarr";
    public string Version => "1.0.0";
    public string Description => "Sends movie requests directly to Radarr.";
    public bool IsSystemPlugin => true;
    public string Type => "Request";
    public string ExternalConfigurationHint => "Add your Radarr connection under External Connections → Request Servers. This plugin only enables the integration.";

    public string[] SupportedMediaTypes => new[] { "Movie" };

    public RadarrRequestProvider(HttpClient httpClient, ILogger<RadarrRequestProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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

            using var client = CreateClient(host, port, useSsl, urlBase, apiKey);

            var payload = new
            {
                title = title,
                tmdbId = int.Parse(tmdbId, System.Globalization.CultureInfo.InvariantCulture),
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

            if (response.IsSuccessStatusCode) return true;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Radarr answers 400 when the film is already in its library. The request did
            // reach Radarr and the film is there, so that is a success for us, not a failure.
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && body.Contains("MovieExistsValidator", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Radarr already has \"{Title}\" (TMDb {TmdbId}); treating the request as sent.", title, tmdbId);
                return true;
            }

            _logger.LogWarning("Radarr at {Host}:{Port} refused \"{Title}\" (TMDb {TmdbId}) with {Status}: {Body}", host, port, title, tmdbId, (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sending \"{Title}\" (TMDb {TmdbId}) to Radarr at {Host}:{Port} failed.", title, tmdbId, host, port);
            return false;
        }
    }
}
