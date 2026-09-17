using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.FanartTv;

public class FanartTvMusicArtworkProvider : IMusicArtworkProvider
{
    public const string HttpClientName = "FanartTvMusicHttpClient";
    public const string MusicBrainzLookupHttpClientName = "MusicBrainzHttpClient";

    private const string MbArtistSearchUrlTemplate = "https://musicbrainz.org/ws/2/artist/?query={0}&fmt=json&limit=1";
    private const string FanartMusicUrlTemplate = "https://webservice.fanart.tv/v3/music/{0}?api_key={1}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FanartTvMusicArtworkProvider> _logger;

    public const string SharedSettingsPluginId = "fanart_artwork";

    public string Id => "fanart_music_artwork";
    public string Name => "Fanart.tv Music Artwork";
    public string ProviderName => "Fanart.tv";
    public string Version => "1.0.0";
    public string Description => "Fetches high-quality artist photos, logos and backgrounds from Fanart.tv. Reuses the Project API key configured on the Fanart.tv Artwork plugin.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Music };

    public FanartTvMusicArtworkProvider(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, ILogger<FanartTvMusicArtworkProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return Array.Empty<PluginSettingDefinitionDto>();
    }

    private async Task<string?> GetApiKeyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        return await settings.GetSettingAsync(SharedSettingsPluginId, "api_key");
    }

    public Task<IReadOnlyList<MusicArtworkResult>> SearchAlbumArtworkAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<MusicArtworkResult>>(Array.Empty<MusicArtworkResult>());
    }

    public async Task<IReadOnlyList<MusicArtworkResult>> SearchArtistArtworkAsync(string artistName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return Array.Empty<MusicArtworkResult>();

        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("Fanart.tv music: api_key not configured, skipping.");
            return Array.Empty<MusicArtworkResult>();
        }

        var mbid = await ResolveArtistMbidAsync(artistName, cancellationToken);
        if (string.IsNullOrEmpty(mbid)) return Array.Empty<MusicArtworkResult>();

        var url = string.Format(FanartMusicUrlTemplate, mbid, apiKey);
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<MusicArtworkResult>();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var results = new List<MusicArtworkResult>();
            ExtractImages(root, "artistthumb", results);
            ExtractImages(root, "artistbackground", results);
            ExtractImages(root, "hdmusiclogo", results);
            ExtractImages(root, "musiclogo", results);
            ExtractImages(root, "musicbanner", results);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fanart.tv music lookup failed for artist {Artist}", artistName);
            return Array.Empty<MusicArtworkResult>();
        }
    }

    private async Task<string?> ResolveArtistMbidAsync(string artistName, CancellationToken cancellationToken)
    {
        try
        {
            var mbClient = _httpClientFactory.CreateClient(MusicBrainzLookupHttpClientName);
            var query = $"artist:\"{EscapeLucene(artistName)}\"";
            var url = string.Format(MbArtistSearchUrlTemplate, Uri.EscapeDataString(query));

            using var response = await mbClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var artist in artists.EnumerateArray())
            {
                if (artist.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    var id = idEl.GetString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MusicBrainz artist MBID lookup failed for {Artist}", artistName);
        }

        return null;
    }

    private void ExtractImages(JsonElement root, string propertyName, List<MusicArtworkResult> results)
    {
        if (!root.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array) return;

        foreach (var item in items.EnumerateArray())
        {
            var imageUrl = item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(imageUrl)) continue;

            results.Add(new MusicArtworkResult
            {
                Url = imageUrl,
                ThumbnailUrl = imageUrl,
                ProviderName = ProviderName
            });
        }
    }

    private static string EscapeLucene(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c == '\\' || c == '"' || c == ':' || c == '+' || c == '-' || c == '!'
                || c == '(' || c == ')' || c == '{' || c == '}' || c == '['
                || c == ']' || c == '^' || c == '~' || c == '*' || c == '?'
                || c == '|' || c == '&' || c == '/')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
