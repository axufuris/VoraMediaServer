using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Deezer;

// Deezer's public catalogue API: no key, and every track and album carries the
// label-supplied explicit flag. Shapes checked against the live API — a track
// has explicit_lyrics (bool) and explicit_content_lyrics (0 not explicit,
// 1 explicit, 2 unknown, 3 edited, 6 no advice); an unknown ISRC is HTTP 200
// with error code 800; a spent quota is HTTP 200 with error code 4.
public class DeezerContentRatingProvider : IMusicContentRatingProvider
{
    public const string HttpClientName = "DeezerHttpClient";
    private const string BaseUrl = "https://api.deezer.com/";
    private const int NoDataErrorCode = 800;

    // A fielded album search is tight, but a popular title still returns
    // compilations and tributes. More than a handful is never the same album.
    private const int MaxEditions = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DeezerContentRatingProvider> _logger;

    public string Id => "deezer_content_ratings";
    public string Name => "Deezer Explicit Ratings";
    public string ProviderName => "Deezer";
    public string Version => "1.0.0";
    public string Description => "Fills in Clean / Explicit for music files that carry no advisory tag, from Deezer's catalogue. Files are matched by ISRC where they have one, which identifies the exact recording. Without one, every edition of the album is checked and the strictest answer wins, so an explicit song is never labelled Clean because a clean edition exists. A rating read from the file's own tags always takes precedence. No account or key needed.";
    public bool IsSystemPlugin => true;
    public string Type => "ContentRatings";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Music };

    public DeezerContentRatingProvider(IHttpClientFactory httpClientFactory, ILogger<DeezerContentRatingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Array.Empty<PluginSettingDefinitionDto>();

    public async Task<TrackAdvisoryLookup> GetTrackAdvisoryByIsrcAsync(string isrc, CancellationToken cancellationToken)
    {
        var (outcome, root) = await GetAsync($"track/isrc:{Uri.EscapeDataString(isrc.Trim())}", cancellationToken);
        using (root)
        {
            if (outcome != ContentRatingLookupOutcome.Found || root == null) return Lookup(outcome);
            return new TrackAdvisoryLookup
            {
                Outcome = ContentRatingLookupOutcome.Found,
                Advisory = ReadAdvisory(root.RootElement)
            };
        }
    }

    public async Task<AlbumEditionsLookup> GetAlbumEditionsAsync(string artistName, string albumTitle, CancellationToken cancellationToken)
    {
        var query = $"artist:\"{StripQuotes(artistName)}\" album:\"{StripQuotes(albumTitle)}\"";
        var (outcome, search) = await GetAsync($"search/album?q={Uri.EscapeDataString(query)}&limit={MaxEditions}", cancellationToken);

        var candidates = new List<(long Id, string Title, string Artist)>();
        using (search)
        {
            if (outcome != ContentRatingLookupOutcome.Found || search == null) return Editions(outcome);
            if (search.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var album in data.EnumerateArray())
                {
                    if (!album.TryGetProperty("id", out var id) || !id.TryGetInt64(out var albumId)) continue;
                    var title = album.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var artist = album.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) continue;
                    candidates.Add((albumId, title, artist));
                }
            }
        }

        if (candidates.Count == 0) return AlbumEditionsLookup.NotFound;

        var editions = new List<ProviderAlbumEdition>();
        foreach (var candidate in candidates.Take(MaxEditions))
        {
            var (tracksOutcome, tracks) = await GetAsync($"album/{candidate.Id}/tracks?limit=300", cancellationToken);
            using (tracks)
            {
                if (tracksOutcome == ContentRatingLookupOutcome.Unavailable) return AlbumEditionsLookup.Unavailable;
                if (tracks == null) continue;
                editions.Add(new ProviderAlbumEdition
                {
                    Title = candidate.Title,
                    ArtistName = candidate.Artist,
                    Tracks = ReadTracks(tracks.RootElement)
                });
            }
        }

        return new AlbumEditionsLookup { Outcome = ContentRatingLookupOutcome.Found, Editions = editions };
    }

    internal static ProviderAdvisory ReadAdvisory(JsonElement item)
    {
        if (item.TryGetProperty("explicit_lyrics", out var flag) && flag.ValueKind == JsonValueKind.True)
        {
            return ProviderAdvisory.Explicit;
        }

        if (!item.TryGetProperty("explicit_content_lyrics", out var code) || !code.TryGetInt32(out var value))
        {
            return ProviderAdvisory.Unknown;
        }

        return value switch
        {
            1 => ProviderAdvisory.Explicit,
            0 or 3 => ProviderAdvisory.Clean,
            _ => ProviderAdvisory.Unknown
        };
    }

    private static IReadOnlyList<ProviderEditionTrack> ReadTracks(JsonElement root)
    {
        var tracks = new List<ProviderEditionTrack>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return tracks;

        foreach (var track in data.EnumerateArray())
        {
            var title = track.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;
            tracks.Add(new ProviderEditionTrack
            {
                Title = title,
                DurationSeconds = track.TryGetProperty("duration", out var d) && d.TryGetInt32(out var seconds) ? seconds : null,
                Isrc = track.TryGetProperty("isrc", out var i) ? i.GetString() : null,
                Advisory = ReadAdvisory(track)
            });
        }

        return tracks;
    }

    private async Task<(ContentRatingLookupOutcome Outcome, JsonDocument? Body)> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(BaseUrl + path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Deezer answered {Status} for {Path}", (int)response.StatusCode, path);
                return (ContentRatingLookupOutcome.Unavailable, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var errorCode = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var parsed) ? parsed : 0;
                document.Dispose();
                if (errorCode == NoDataErrorCode) return (ContentRatingLookupOutcome.NotFound, null);

                _logger.LogWarning("Deezer returned error {Code} for {Path}", errorCode, path);
                return (ContentRatingLookupOutcome.Unavailable, null);
            }

            return (ContentRatingLookupOutcome.Found, document);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Deezer request failed for {Path}", path);
            return (ContentRatingLookupOutcome.Unavailable, null);
        }
    }

    private static TrackAdvisoryLookup Lookup(ContentRatingLookupOutcome outcome) =>
        outcome == ContentRatingLookupOutcome.NotFound ? TrackAdvisoryLookup.NotFound : TrackAdvisoryLookup.Unavailable;

    private static AlbumEditionsLookup Editions(ContentRatingLookupOutcome outcome) =>
        outcome == ContentRatingLookupOutcome.NotFound ? AlbumEditionsLookup.NotFound : AlbumEditionsLookup.Unavailable;

    private static string StripQuotes(string value) => value.Replace("\"", string.Empty).Trim();
}
