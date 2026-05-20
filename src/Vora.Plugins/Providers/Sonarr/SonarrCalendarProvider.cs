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
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public SonarrCalendarProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "sonarr_url",
                Label = "Sonarr URL",
                Type = "text",
                Description = "The base URL to your Sonarr instance (e.g., http://192.168.1.100:8989)"
            },
            new PluginSettingDefinitionDto
            {
                Key = "sonarr_api_key",
                Label = "Sonarr API Key",
                Type = "password",
                Description = "Your Sonarr API key found in Settings > General"
            }
        };
    }

    public async Task<IEnumerable<CalendarEventDto>> GetEventsAsync(DateTime startDate, DateTime endDate)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var url = await settings.GetSettingAsync(Id, "sonarr_url");
        var apiKey = await settings.GetSettingAsync(Id, "sonarr_api_key");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            return new List<CalendarEventDto>();

        url = url.TrimEnd('/');
        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}/api/v3/calendar?start={startStr}&end={endStr}&includeSeries=true");
        request.Headers.Add("X-Api-Key", apiKey);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<CalendarEventDto>();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var events = new List<CalendarEventDto>();

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

                        events.Add(new CalendarEventDto
                        {
                            Id = $"sonarr_{id}",
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

            return events;
        }
        catch
        {
            return new List<CalendarEventDto>();
        }
    }
}
