using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Sonarr;

public class SonarrCalendarProvider : ICalendarProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "sonarr_calendar";
    public string Name => "Sonarr Calendar";
    public string Description => "Fetches upcoming TV episodes and download status directly from your Sonarr instance.";
    public string Version => "1.0.0";
    public string Type => "Calendar";
    public string ExternalConfigurationHint => "Reads from Request Servers tagged \"Use for Release Calendar\" under System Settings → Request Servers.";
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public SonarrCalendarProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
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
        var servers = await lookup.GetCalendarServersAsync("sonarr_requester");

        if (servers.Count == 0) return new List<CalendarEventDto>();

        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");

        var combined = new List<CalendarEventDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.BaseUrl) || string.IsNullOrWhiteSpace(server.ApiKey)) continue;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUrl.TrimEnd('/')}/api/v3/calendar?start={startStr}&end={endStr}&includeSeries=true");
            request.Headers.Add("X-Api-Key", server.ApiKey);

            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                foreach (var episode in doc.RootElement.EnumerateArray())
                {
                    string showTitle = "Unknown Show";
                    string? tmdbId = null;

                    if (episode.TryGetProperty("series", out var series) && series.ValueKind != JsonValueKind.Null)
                    {
                        showTitle = series.TryGetProperty("title", out var st) && st.ValueKind != JsonValueKind.Null ? st.GetString() ?? "Unknown Show" : "Unknown Show";
                        tmdbId = series.TryGetProperty("tmdbId", out var tmdb) && tmdb.ValueKind == JsonValueKind.Number ? tmdb.GetInt32().ToString() : null;
                    }

                    var epTitle = episode.TryGetProperty("title", out var et) && et.ValueKind != JsonValueKind.Null ? et.GetString() ?? "TBA" : "TBA";
                    var seasonNumber = episode.TryGetProperty("seasonNumber", out var sn) && sn.ValueKind == JsonValueKind.Number ? sn.GetInt32() : 0;
                    var episodeNumber = episode.TryGetProperty("episodeNumber", out var en) && en.ValueKind == JsonValueKind.Number ? en.GetInt32() : 0;
                    var hasFile = episode.TryGetProperty("hasFile", out var hf) && hf.GetBoolean();
                    var id = episode.TryGetProperty("id", out var eid) && eid.ValueKind == JsonValueKind.Number ? eid.GetInt32().ToString() : Guid.NewGuid().ToString();

                    if (episode.TryGetProperty("airDateUtc", out var airDate) && airDate.ValueKind != JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(airDate.GetString(), out var parsedDate))
                        {
                            var utcDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                            var dtoId = $"sonarr_{id}";
                            if (seenIds.Add(dtoId))
                            {
                                combined.Add(new CalendarEventDto
                                {
                                    Id = dtoId,
                                    ExternalId = tmdbId,
                                    ExternalProviderId = "tmdb_discovery",
                                    Title = showTitle,
                                    SubTitle = $"S{seasonNumber:D2}E{episodeNumber:D2} - {epTitle}",
                                    MediaType = "Episode",
                                    ReleaseDate = utcDate,
                                    AirTime = utcDate.TimeOfDay,
                                    ReleaseType = "TV Airing",
                                    ContentRating = "Unrated",
                                    IsInLibrary = hasFile,
                                    IsWatchlisted = false
                                });
                            }
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
}
