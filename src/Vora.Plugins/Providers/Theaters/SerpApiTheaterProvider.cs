using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Theaters;

public class SerpApiTheaterProvider : IDiscoveryTheaterProvider
{
    private const long CacheEntrySize = 1024;

    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public string Id => "serpapi_theater";
    public string Name => "SerpApi Google Showtimes";
    public string ProviderName => "Google Showtimes";
    public string Version => "1.0.0";
    public string Description => "Scrapes Google Search to find local theater showtimes. Requires a free API key from serpapi.com.";
    public bool IsSystemPlugin => true;
    public string Type => "Theater";

    public SerpApiTheaterProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _httpClient.BaseAddress = new Uri("https://serpapi.com/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "SerpApi Key",
                Type = "password",
                Description = "SerpApi Private API Key. Sign up at https://serpapi.com/users/sign_up (free tier = 100 searches/month). After signing in, copy the 'Private API Key' from https://serpapi.com/manage-api-key. Showtimes are cached per movie + location + date to minimize search usage."
            },
            new PluginSettingDefinitionDto
            {
                Key = "default_location",
                Label = "Admin Default Zipcode/City",
                Type = "text",
                Description = "The default location to search if the user has not set a Zipcode in their Client Settings."
            },
            new PluginSettingDefinitionDto
            {
                Key = "max_theaters",
                Label = "Admin Default Max Theaters",
                Type = "number",
                Description = "Maximum number of theaters to return. Defaults to 6."
            },
            new PluginSettingDefinitionDto
            {
                Key = "auto_showtimes",
                Label = "Auto-Load Showtimes",
                Type = "boolean",
                Description = "Automatically fetch showtimes when a movie page loads. Turn off to show a manual load button (saves API calls)."
            }
        };
    }

    public async Task<bool> IsAutoLoadEnabledAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var val = await settings.GetSettingAsync(Id, "auto_showtimes");
        return string.IsNullOrWhiteSpace(val) || val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<TheaterDto>> GetShowtimesAsync(string movieTitle, string location, DateTime date, int? maxTheaters = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var searchLocation = string.IsNullOrWhiteSpace(location)
            ? await settings.GetSettingAsync(Id, "default_location")
            : location;

        if (string.IsNullOrWhiteSpace(searchLocation)) return new List<TheaterDto>();

        var limitStr = await settings.GetSettingAsync(Id, "max_theaters");
        var limit = maxTheaters ?? (int.TryParse(limitStr, out var l) ? l : 6);

        var cacheKey = $"serpapi_theaters_{movieTitle.ToLowerInvariant()}_{searchLocation.ToLowerInvariant()}_{date:yyyyMMdd}";

        var cachedTheaters = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            entry.Size = CacheEntrySize;

            var apiKey = await settings.GetSettingAsync(Id, "api_key");
            if (string.IsNullOrWhiteSpace(apiKey)) return new List<TheaterDto>();

            var query = $"{movieTitle} showtimes {searchLocation}";
            var url = $"search.json?engine=google&q={Uri.EscapeDataString(query)}&api_key={apiKey}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<TheaterDto>();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var el = doc.RootElement;

            var theaters = new List<TheaterDto>();

            if (el.TryGetProperty("showtimes", out var showtimesArray) && showtimesArray.ValueKind == JsonValueKind.Array)
            {
                if (showtimesArray.GetArrayLength() > 0 && showtimesArray[0].TryGetProperty("theaters", out var theatersData))
                {
                    foreach (var theaterNode in theatersData.EnumerateArray())
                    {
                        var theater = new TheaterDto
                        {
                            Name = theaterNode.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown Theater" : "Unknown Theater",
                            Address = theaterNode.TryGetProperty("address", out var a) ? a.GetString() ?? "" : ""
                        };

                        if (theaterNode.TryGetProperty("showing", out var showings))
                        {
                            foreach (var show in showings.EnumerateArray())
                            {
                                var timeList = show.TryGetProperty("time", out var t) ? t.EnumerateArray().Select(x => x.GetString()).ToList() : new List<string?>();

                                foreach (var time in timeList)
                                {
                                    if (!string.IsNullOrEmpty(time))
                                    {
                                        theater.Showtimes.Add(new ShowtimeDto
                                        {
                                            Time = time,
                                            Format = show.TryGetProperty("type", out var typeNode) ? typeNode.GetString() ?? "Standard" : "Standard"
                                        });
                                    }
                                }
                            }
                        }

                        if (theater.Showtimes.Any()) theaters.Add(theater);
                    }
                }
            }
            return theaters;
        });

        return cachedTheaters?.Take(limit) ?? new List<TheaterDto>();
    }
}
