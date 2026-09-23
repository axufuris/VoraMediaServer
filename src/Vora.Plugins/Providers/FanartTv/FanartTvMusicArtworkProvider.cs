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
    private const string MbReleaseGroupSearchUrlTemplate = "https://musicbrainz.org/ws/2/release-group/?query={0}&fmt=json&limit=1";
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

    // Fanart returns an artist's albums keyed by RELEASE-GROUP mbid in the same
    // payload as the artist images, so album artwork needs that id as well as the
    // artist's. Both are cached, so the pair is resolved once per album ever.
    public async Task<IReadOnlyList<MusicArtworkResult>> SearchAlbumArtworkAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle)) return Array.Empty<MusicArtworkResult>();

        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("Fanart.tv music: api_key not configured, skipping.");
            return Array.Empty<MusicArtworkResult>();
        }

        var artistMbid = await ResolveArtistMbidAsync(artistName, cancellationToken);
        if (string.IsNullOrEmpty(artistMbid)) return Array.Empty<MusicArtworkResult>();

        var releaseGroupMbid = await ResolveReleaseGroupMbidAsync(artistName, albumTitle, cancellationToken);
        if (string.IsNullOrEmpty(releaseGroupMbid)) return Array.Empty<MusicArtworkResult>();

        var url = string.Format(FanartMusicUrlTemplate, artistMbid, apiKey);
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<MusicArtworkResult>();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("albums", out var albums) || albums.ValueKind != JsonValueKind.Object)
                return Array.Empty<MusicArtworkResult>();

            if (!albums.TryGetProperty(releaseGroupMbid, out var album) || album.ValueKind != JsonValueKind.Object)
                return Array.Empty<MusicArtworkResult>();

            var results = new List<MusicArtworkResult>();
            ExtractImages(album, "albumcover", MusicArtworkKind.Cover, results);
            // Fanart calls it cdart; Vora's slot is Disc Art, and the modal browses
            // it with the Logo kind because both are a disc-or-wordmark overlay
            // rather than a cover.
            ExtractImages(album, "cdart", MusicArtworkKind.Logo, results);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fanart.tv album lookup failed for {Artist} - {Album}", artistName, albumTitle);
            return Array.Empty<MusicArtworkResult>();
        }
    }

    private async Task<string?> ResolveReleaseGroupMbidAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        var cached = await WithCacheAsync(cache => cache.GetAlbumIdAsync(artistName, albumTitle, cancellationToken));
        if (!string.IsNullOrEmpty(cached)) return cached;

        try
        {
            var mbClient = _httpClientFactory.CreateClient(MusicBrainzLookupHttpClientName);
            var query = $"artist:\"{EscapeLucene(artistName)}\" AND releasegroup:\"{EscapeLucene(albumTitle)}\"";
            var url = string.Format(MbReleaseGroupSearchUrlTemplate, Uri.EscapeDataString(query));

            using var response = await mbClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("release-groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var group in groups.EnumerateArray())
            {
                if (group.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    var id = idEl.GetString();
                    if (string.IsNullOrEmpty(id)) continue;
                    await WithCacheAsync(cache => cache.SetAlbumIdAsync(artistName, albumTitle, id, cancellationToken));
                    return id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MusicBrainz release-group lookup failed for {Artist} - {Album}", artistName, albumTitle);
        }

        return null;
    }

    private async Task<T?> WithCacheAsync<T>(Func<IMusicBrainzIdCache, Task<T>> read)
    {
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetService<IMusicBrainzIdCache>();
        return cache == null ? default : await read(cache);
    }

    private async Task WithCacheAsync(Func<IMusicBrainzIdCache, Task> write)
    {
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetService<IMusicBrainzIdCache>();
        if (cache != null) await write(cache);
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
            ExtractImages(root, "artistthumb", MusicArtworkKind.Thumb, results);
            ExtractImages(root, "artistbackground", MusicArtworkKind.Background, results);
            ExtractImages(root, "hdmusiclogo", MusicArtworkKind.Logo, results);
            ExtractImages(root, "musiclogo", MusicArtworkKind.Logo, results);
            ExtractImages(root, "musicbanner", MusicArtworkKind.Banner, results);

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
        var cached = await WithCacheAsync(cache => cache.GetArtistIdAsync(artistName, cancellationToken));
        if (!string.IsNullOrEmpty(cached)) return cached;

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
                    if (string.IsNullOrEmpty(id)) continue;
                    await WithCacheAsync(cache => cache.SetArtistIdAsync(artistName, id, cancellationToken));
                    return id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MusicBrainz artist MBID lookup failed for {Artist}", artistName);
        }

        return null;
    }

    private void ExtractImages(JsonElement root, string propertyName, MusicArtworkKind kind, List<MusicArtworkResult> results)
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
                ProviderName = ProviderName,
                Kind = kind
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
