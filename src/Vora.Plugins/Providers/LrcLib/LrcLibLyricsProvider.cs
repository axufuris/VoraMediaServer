using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.LrcLib;

public class LrcLibLyricsProvider : ILyricsProvider
{
    public const string HttpClientName = "LrcLibHttpClient";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LrcLibLyricsProvider> _logger;

    public string Id => "lrclib_lyrics";
    public string Name => "LRClib Lyrics";
    public string ProviderName => "LRClib";
    public string Version => "1.0.0";
    public string Description => "Fetches plain and time-synced lyrics from LRClib. No API key required.";
    public bool IsSystemPlugin => true;
    public string Type => "Lyrics";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Music };

    public LrcLibLyricsProvider(IHttpClientFactory httpClientFactory, ILogger<LrcLibLyricsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return Array.Empty<PluginSettingDefinitionDto>();
    }

    public async Task<LyricsResult?> GetLyricsAsync(string artistName, string trackTitle, string? albumTitle, int? durationSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackTitle)) return null;

        var queryParts = new List<string>
        {
            $"artist_name={Uri.EscapeDataString(artistName)}",
            $"track_name={Uri.EscapeDataString(trackTitle)}"
        };
        if (!string.IsNullOrWhiteSpace(albumTitle))
            queryParts.Add($"album_name={Uri.EscapeDataString(albumTitle)}");
        if (durationSeconds.HasValue && durationSeconds.Value > 0)
            queryParts.Add($"duration={durationSeconds.Value}");

        var url = "https://lrclib.net/api/get?" + string.Join("&", queryParts);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var plain = root.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var synced = root.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var lrcId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString() : null;

            if (string.IsNullOrWhiteSpace(plain) && string.IsNullOrWhiteSpace(synced)) return null;

            return new LyricsResult
            {
                PlainLyrics = plain,
                SyncedLyrics = synced,
                ProviderName = ProviderName,
                SourceUrl = lrcId != null ? $"https://lrclib.net/lyrics/{lrcId}" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LRClib lookup failed for {Artist} / {Track}", artistName, trackTitle);
            return null;
        }
    }
}
