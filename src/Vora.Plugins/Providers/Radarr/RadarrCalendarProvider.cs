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
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public RadarrCalendarProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
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
                Key = "radarr_url",
                Label = "Radarr URL",
                Type = "text",
                Description = "The base URL to your Radarr instance (e.g., http://192.168.1.100:7878)"
            },
            new PluginSettingDefinitionDto
            {
                Key = "radarr_api_key",
                Label = "Radarr API Key",
                Type = "password",
                Description = "Your Radarr API key found in Settings > General"
            }
        };
    }

    public async Task<IEnumerable<CalendarEventDto>> GetEventsAsync(DateTime startDate, DateTime endDate)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var url = await settings.GetSettingAsync(Id, "radarr_url");
        var apiKey = await settings.GetSettingAsync(Id, "radarr_api_key");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            return new List<CalendarEventDto>();

        url = url.TrimEnd('/');
        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}/api/v3/calendar?start={startStr}&end={endStr}");
        request.Headers.Add("X-Api-Key", apiKey);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<CalendarEventDto>();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var events = new List<CalendarEventDto>();

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
                        events.Add(CreateDto(id, tmdbId, title, cinemaDate, "Theatrical", hasFile));
                    }
                }

                if (movie.TryGetProperty("digitalRelease", out var digital) && digital.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(digital.GetString(), out var digitalDate) && digitalDate >= startDate && digitalDate <= endDate)
                    {
                        events.Add(CreateDto(id, tmdbId, title, digitalDate, "Digital", hasFile));
                    }
                }

                if (movie.TryGetProperty("physicalRelease", out var physical) && physical.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(physical.GetString(), out var physicalDate) && physicalDate >= startDate && physicalDate <= endDate)
                    {
                        events.Add(CreateDto(id, tmdbId, title, physicalDate, "Physical", hasFile));
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
