using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.MusicBrainz;

public class MusicBrainzArtworkProvider : IMusicArtworkProvider
{
    public const string HttpClientName = "MusicBrainzHttpClient";

    private const string MbSearchUrlTemplate = "https://musicbrainz.org/ws/2/release-group/?query={0}&fmt=json&limit={1}";
    private const string CaaReleaseGroupUrlTemplate = "https://coverartarchive.org/release-group/{0}";
    private const int MaxReleaseGroupsToProbe = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MusicBrainzArtworkProvider> _logger;

    public string Id => "musicbrainz_artwork";
    public string Name => "MusicBrainz Artwork";
    public string ProviderName => "MusicBrainz";
    public string Version => "1.0.0";
    public string Description => "Fetches album cover art from MusicBrainz / Cover Art Archive. Public service, no API key required.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Music" };

    public MusicBrainzArtworkProvider(IHttpClientFactory httpClientFactory, ILogger<MusicBrainzArtworkProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return Array.Empty<PluginSettingDefinitionDto>();
    }

    public async Task<IReadOnlyList<MusicArtworkResult>> SearchAlbumArtworkAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
            return Array.Empty<MusicArtworkResult>();

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var query = $"artist:\"{EscapeLucene(artistName)}\" AND releasegroup:\"{EscapeLucene(albumTitle)}\"";
        var url = string.Format(MbSearchUrlTemplate, Uri.EscapeDataString(query), MaxReleaseGroupsToProbe);

        var mbids = new List<string>();
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("release-groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in groups.EnumerateArray())
                {
                    if (group.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrEmpty(id)) mbids.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MusicBrainz search failed for {Artist} / {Album}", artistName, albumTitle);
            return Array.Empty<MusicArtworkResult>();
        }

        var results = new List<MusicArtworkResult>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mbid in mbids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var caaUrl = string.Format(CaaReleaseGroupUrlTemplate, mbid);
                using var response = await client.GetAsync(caaUrl, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var image in images.EnumerateArray())
                {
                    var isFront = image.TryGetProperty("front", out var frontEl) && frontEl.GetBoolean();
                    if (!isFront) continue;

                    var imageUrl = GetString(image, "image");
                    if (string.IsNullOrEmpty(imageUrl)) continue;
                    if (!seenUrls.Add(imageUrl)) continue;

                    string? thumbUrl = null;
                    if (image.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Object)
                    {
                        thumbUrl = GetString(thumbs, "large")
                            ?? GetString(thumbs, "500")
                            ?? GetString(thumbs, "small")
                            ?? GetString(thumbs, "250");
                    }

                    results.Add(new MusicArtworkResult
                    {
                        Url = imageUrl,
                        ThumbnailUrl = thumbUrl ?? imageUrl,
                        ProviderName = ProviderName
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cover Art Archive lookup failed for release-group {Mbid}", mbid);
            }
        }

        return results;
    }

    public Task<IReadOnlyList<MusicArtworkResult>> SearchArtistArtworkAsync(string artistName, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<MusicArtworkResult>>(Array.Empty<MusicArtworkResult>());
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var s = prop.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
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
