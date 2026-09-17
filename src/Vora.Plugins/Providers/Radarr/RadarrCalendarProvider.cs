using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Radarr;

public class RadarrCalendarProvider : ICalendarProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "radarr_calendar";
    public string Name => "Radarr Calendar";
    public string Description => "Fetches upcoming movie releases and download status directly from your Radarr instance.";
    public string Version => "1.0.0";
    public string Type => "Calendar";
    public string ExternalConfigurationHint => "Reads from Request Servers tagged \"Use for Release Calendar\" under System Settings → Request Servers.";
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public RadarrCalendarProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() =>
        new List<PluginSettingDefinitionDto>();

    public async Task<IEnumerable<CalendarEventDto>> GetEventsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IRequestServerLookup>();
        var servers = await lookup.GetCalendarServersAsync("radarr_requester");

        if (servers.Count == 0) return new List<CalendarEventDto>();

        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");

        var combined = new List<CalendarEventDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.BaseUrl) || string.IsNullOrWhiteSpace(server.ApiKey)) continue;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUrl.TrimEnd('/')}/api/v3/calendar?start={startStr}&end={endStr}");
            request.Headers.Add("X-Api-Key", server.ApiKey);

            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                foreach (var movie in doc.RootElement.EnumerateArray())
                {
                    var title = movie.TryGetProperty("title", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
                    var tmdbId = movie.TryGetProperty("tmdbId", out var tmdb) ? tmdb.GetInt32().ToString() : null;
                    var hasFile = movie.TryGetProperty("hasFile", out var hf) && hf.GetBoolean();
                    var id = movie.TryGetProperty("id", out var mid) ? mid.GetInt32().ToString() : Guid.NewGuid().ToString();

                    if (movie.TryGetProperty("inCinemas", out var inCinemas) && inCinemas.ValueKind != JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(inCinemas.GetString(), out var cinemaDate) && cinemaDate >= startDate && cinemaDate <= endDate)
                        {
                            AddDeduped(combined, seenIds, CreateDto(id, tmdbId, title, cinemaDate, "Theatrical", hasFile));
                        }
                    }

                    if (movie.TryGetProperty("digitalRelease", out var digital) && digital.ValueKind != JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(digital.GetString(), out var digitalDate) && digitalDate >= startDate && digitalDate <= endDate)
                        {
                            AddDeduped(combined, seenIds, CreateDto(id, tmdbId, title, digitalDate, "Digital", hasFile));
                        }
                    }

                    if (movie.TryGetProperty("physicalRelease", out var physical) && physical.ValueKind != JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(physical.GetString(), out var physicalDate) && physicalDate >= startDate && physicalDate <= endDate)
                        {
                            AddDeduped(combined, seenIds, CreateDto(id, tmdbId, title, physicalDate, "Physical", hasFile));
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return combined;
    }

    private static void AddDeduped(List<CalendarEventDto> sink, HashSet<string> seen, CalendarEventDto evt)
    {
        if (seen.Add(evt.Id)) sink.Add(evt);
    }

    private CalendarEventDto CreateDto(string id, string? tmdbId, string title, DateTime date, string releaseType, bool hasFile)
    {
        return new CalendarEventDto
        {
            Id = $"radarr_{id}_{releaseType}",
            ExternalId = tmdbId,
            ExternalProviderId = "tmdb_discovery", // Used by CalendarManager to deduplicate with local DB!
            Title = title,
            MediaType = "Movie",
            ReleaseDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            ReleaseType = releaseType,
            ContentRating = "Unrated", // We can leave this unrated, Local DB will overwrite it if we own it
            IsInLibrary = hasFile,
            IsWatchlisted = false
        };
    }
}
