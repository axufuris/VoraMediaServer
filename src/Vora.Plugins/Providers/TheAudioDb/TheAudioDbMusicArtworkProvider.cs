using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.TheAudioDb;

public class TheAudioDbMusicArtworkProvider : IMusicArtworkProvider
{
    public const string HttpClientName = "TheAudioDbHttpClient";
    private const string DefaultApiKey = "2";

    private const string ArtistSearchUrlTemplate = "https://www.theaudiodb.com/api/v1/json/{0}/search.php?s={1}";
    private const string AlbumSearchUrlTemplate = "https://www.theaudiodb.com/api/v1/json/{0}/searchalbum.php?s={1}&a={2}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TheAudioDbMusicArtworkProvider> _logger;

    public string Id => "theaudiodb_artwork";
    public string Name => "TheAudioDB Artwork";
    public string ProviderName => "TheAudioDB";
    public string Version => "1.0.0";
    public string Description => "Fetches artist photos and album covers from TheAudioDB. Defaults to the public test API key; provide your own for production use.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Music };

    public TheAudioDbMusicArtworkProvider(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, ILogger<TheAudioDbMusicArtworkProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "TheAudioDB API Key",
                Type = "password",
                Required = false,
                DefaultValue = DefaultApiKey,
                Description = "TheAudioDB API key. The default value \"2\" is the public test key (rate-limited; fine for trying it out). For production use, support the project on Patreon at https://www.patreon.com/theaudiodb — supporters at the $1+/month tier receive a personal API key via Patreon DM. See https://www.theaudiodb.com/api_guide.php for current details."
            }
        };
    }

    private async Task<string> GetApiKeyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var key = await settings.GetSettingAsync(Id, "api_key");
        return string.IsNullOrWhiteSpace(key) ? DefaultApiKey : key;
    }

    public async Task<IReadOnlyList<MusicArtworkResult>> SearchAlbumArtworkAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
            return Array.Empty<MusicArtworkResult>();

        var apiKey = await GetApiKeyAsync();
        var url = string.Format(AlbumSearchUrlTemplate, apiKey, Uri.EscapeDataString(artistName), Uri.EscapeDataString(albumTitle));

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<MusicArtworkResult>();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("album", out var albums) || albums.ValueKind != JsonValueKind.Array)
                return Array.Empty<MusicArtworkResult>();

            var results = new List<MusicArtworkResult>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var album in albums.EnumerateArray())
            {
                AddIfPresent(album, "strAlbumThumbHQ", MusicArtworkKind.Cover, results, seenUrls);
                AddIfPresent(album, "strAlbumThumb", MusicArtworkKind.Cover, results, seenUrls);
                AddIfPresent(album, "strAlbumThumbBack", MusicArtworkKind.Cover, results, seenUrls);
                AddIfPresent(album, "strAlbum3DCase", MusicArtworkKind.Cover, results, seenUrls);
                AddIfPresent(album, "strAlbum3DFlat", MusicArtworkKind.Cover, results, seenUrls);
                AddIfPresent(album, "strAlbumCDart", MusicArtworkKind.Logo, results, seenUrls);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TheAudioDB album lookup failed for {Artist} / {Album}", artistName, albumTitle);
            return Array.Empty<MusicArtworkResult>();
        }
    }

    public async Task<IReadOnlyList<MusicArtworkResult>> SearchArtistArtworkAsync(string artistName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return Array.Empty<MusicArtworkResult>();

        var apiKey = await GetApiKeyAsync();
        var url = string.Format(ArtistSearchUrlTemplate, apiKey, Uri.EscapeDataString(artistName));

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<MusicArtworkResult>();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
                return Array.Empty<MusicArtworkResult>();

            var results = new List<MusicArtworkResult>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in artists.EnumerateArray())
            {
                AddIfPresent(artist, "strArtistThumb", MusicArtworkKind.Thumb, results, seenUrls);
                AddIfPresent(artist, "strArtistCutout", MusicArtworkKind.Thumb, results, seenUrls);
                AddIfPresent(artist, "strArtistClearart", MusicArtworkKind.Thumb, results, seenUrls);
                AddIfPresent(artist, "strArtistLogo", MusicArtworkKind.Logo, results, seenUrls);
                AddIfPresent(artist, "strArtistBanner", MusicArtworkKind.Banner, results, seenUrls);
                AddIfPresent(artist, "strArtistWideThumb", MusicArtworkKind.Background, results, seenUrls);
                AddIfPresent(artist, "strArtistFanart", MusicArtworkKind.Background, results, seenUrls);
                AddIfPresent(artist, "strArtistFanart2", MusicArtworkKind.Background, results, seenUrls);
                AddIfPresent(artist, "strArtistFanart3", MusicArtworkKind.Background, results, seenUrls);
                AddIfPresent(artist, "strArtistFanart4", MusicArtworkKind.Background, results, seenUrls);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TheAudioDB artist lookup failed for {Artist}", artistName);
            return Array.Empty<MusicArtworkResult>();
        }
    }

    private void AddIfPresent(JsonElement element, string property, MusicArtworkKind kind, List<MusicArtworkResult> results, HashSet<string> seen)
    {
        if (!element.TryGetProperty(property, out var prop)) return;
        if (prop.ValueKind != JsonValueKind.String) return;
        var url = prop.GetString();
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!seen.Add(url)) return;

        results.Add(new MusicArtworkResult
        {
            Url = url,
            ThumbnailUrl = url,
            ProviderName = ProviderName,
            Kind = kind
        });
    }
}
