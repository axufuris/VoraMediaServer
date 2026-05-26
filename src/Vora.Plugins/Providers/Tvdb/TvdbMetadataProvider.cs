using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tvdb;

public class TvdbMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, Dictionary<string, MetadataResult>> _tvdbEpisodeCache = new();

    private readonly bool _fetchExtendedSeasonData = true;

    public string Id => "tvdb_metadata";
    public string Name => "The TV Database (TVDB)";
    public string Version => "1.0.0";
    public string Description => "Official Vora metadata agent for fetching movies, TV shows, and actor details from TVDB.";
    public bool IsSystemPlugin => true;
    public string Type => "Metadata";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ProviderName => "TVDB";

    public TvdbMetadataProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://api4.thetvdb.com/v4/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "TVDB API Key",
                Type = "password",
                Description = "TVDB v4 API key. Requires a subscriber account — sign up at https://thetvdb.com/subscribe. Once subscribed, generate the key at https://thetvdb.com/dashboard/account/apikey and paste it here. Vora exchanges it for a session token automatically and caches that token in plugin settings."
            }
        };
    }

    private async Task<string?> GetValidTokenAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var token = await settings.GetSettingAsync(Id, "tvdb_token");
        if (string.IsNullOrEmpty(token))
        {
            var apiKey = await settings.GetSettingAsync(Id, "api_key");
            if (string.IsNullOrEmpty(apiKey)) return null;

            var loginRequest = new { apikey = apiKey };
            var content = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("login", content);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
                if (token != null) await settings.SetSettingAsync(Id, "tvdb_token", token);
            }
        }
        return token;
    }

    public async Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteSearch(query, "movie", year);
    }

    public async Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteSearch(query, "series", year);
    }

    private async Task<MetadataResult?> ExecuteSearch(string query, string type, int? year)
    {
        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        var url = $"search?query={Uri.EscapeDataString(query)}&type={type}";
        if (year.HasValue) url += $"&year={year.Value}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0) return null;

        var el = data[0];
        return new MetadataResult
        {
            TvdbId = el.TryGetProperty("tvdb_id", out var tid) && tid.ValueKind != JsonValueKind.Null ? tid.GetString() : null,
            Title = el.GetProperty("name").GetString(),
            Overview = el.TryGetProperty("overview", out var ov) && ov.ValueKind != JsonValueKind.Null ? ov.GetString() : null,
            PosterUrl = el.TryGetProperty("image_url", out var img) && img.ValueKind != JsonValueKind.Null ? img.GetString() : null,
            ReleaseDate = DateTime.TryParse(el.TryGetProperty("first_air_time", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : "", out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null
        };
    }

    public async Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        string tvdbIdToFetch = id;

        if (source.Equals("imdb", StringComparison.OrdinalIgnoreCase))
        {
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, $"search?query={id}&type=movie");
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var searchRes = await _httpClient.SendAsync(searchReq);
            if (!searchRes.IsSuccessStatusCode) return null;

            using var searchStream = await searchRes.Content.ReadAsStreamAsync();
            using var searchDoc = await JsonDocument.ParseAsync(searchStream);
            if (!searchDoc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.GetArrayLength() == 0) return null;

            var tvdbIdElement = dataArr[0].GetProperty("tvdb_id");
            tvdbIdToFetch = tvdbIdElement.ValueKind == JsonValueKind.Number ? tvdbIdElement.GetInt32().ToString() : tvdbIdElement.GetString() ?? "";

            if (string.IsNullOrEmpty(tvdbIdToFetch)) return null;
        }
        else if (!source.Equals("tvdb", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var url = $"movies/{tvdbIdToFetch}/extended";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.GetProperty("data");

        var result = new MetadataResult
        {
            TvdbId = tvdbIdToFetch,
            Title = data.TryGetProperty("name", out var n) && n.ValueKind != JsonValueKind.Null ? n.GetString() : null,
            Overview = data.TryGetProperty("overview", out var ov) && ov.ValueKind != JsonValueKind.Null ? ov.GetString() : null,
            PosterUrl = data.TryGetProperty("image", out var img) && img.ValueKind != JsonValueKind.Null ? img.GetString() : null,
            RuntimeMinutes = data.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt32() : null,
        };

        if (data.TryGetProperty("first_release", out var firstReleaseObj) && firstReleaseObj.ValueKind == JsonValueKind.Object)
        {
            var releaseDateStr = firstReleaseObj.TryGetProperty("date", out var fr) && fr.ValueKind != JsonValueKind.Null ? fr.GetString() : null;
            if (!string.IsNullOrEmpty(releaseDateStr) && DateTime.TryParse(releaseDateStr, out var d))
            {
                result.ReleaseDate = DateTime.SpecifyKind(d, DateTimeKind.Utc);
            }
        }

        if (data.TryGetProperty("contentRatings", out var crList) && crList.ValueKind == JsonValueKind.Array && crList.GetArrayLength() > 0)
        {
            var usRating = crList.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("country", out var cc) && cc.GetString() == "usa");
            if (usRating.ValueKind != JsonValueKind.Undefined && usRating.TryGetProperty("name", out var rating))
            {
                result.ContentRating = rating.GetString();
            }
        }

        if (data.TryGetProperty("characters", out var chars) && chars.ValueKind == JsonValueKind.Array)
        {
            foreach (var character in chars.EnumerateArray())
            {
                result.Cast.Add(new CastMemberResult
                {
                    TmdbId = 0,

                    Name = character.TryGetProperty("personName", out var pn) && pn.ValueKind != JsonValueKind.Null ? pn.GetString() ?? "Unknown" : "Unknown",
                    CharacterName = character.TryGetProperty("name", out var cn) && cn.ValueKind != JsonValueKind.Null ? cn.GetString() : null,

                    ProfileImageUrl = character.TryGetProperty("personImgURL", out var pimg) && pimg.ValueKind != JsonValueKind.Null ? pimg.GetString() :
                                      (character.TryGetProperty("image", out var cimg) && cimg.ValueKind != JsonValueKind.Null ? cimg.GetString() : null),

                    Roles = MapPeopleTypeToRole(character.TryGetProperty("peopleType", out var pt) && pt.ValueKind != JsonValueKind.Null ? pt.GetString() : null)
                });
            }
        }

        if (data.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genres.EnumerateArray())
            {
                if (genre.TryGetProperty("id", out var gid) && gid.ValueKind == JsonValueKind.Number) result.GenreIds.Add(gid.GetInt32());
            }
        }

        if (data.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                var tagName = tag.TryGetProperty("name", out var tn) && tn.ValueKind != JsonValueKind.Null
                    ? tn.GetString()?.ToLower().Replace(" ", "").Replace("-", "")
                    : "";

                if (tagName == "duringcreditsstinger" || tagName == "midcreditsstinger")
                    result.HasMidCreditsStinger = true;

                if (tagName == "aftercreditsstinger" || tagName == "postcreditsstinger")
                    result.HasPostCreditsStinger = true;
            }
        }

        return result;
    }

    public async Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        string tvdbIdToFetch = id;

        if (source.Equals("imdb", StringComparison.OrdinalIgnoreCase))
        {
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, $"search?query={id}&type=series");
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var searchRes = await _httpClient.SendAsync(searchReq);
            if (!searchRes.IsSuccessStatusCode) return null;

            using var searchStream = await searchRes.Content.ReadAsStreamAsync();
            using var searchDoc = await JsonDocument.ParseAsync(searchStream);
            if (!searchDoc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.GetArrayLength() == 0) return null;

            var tvdbIdElement = dataArr[0].GetProperty("tvdb_id");
            tvdbIdToFetch = tvdbIdElement.ValueKind == JsonValueKind.Number ? tvdbIdElement.GetInt32().ToString() : tvdbIdElement.GetString() ?? "";

            if (string.IsNullOrEmpty(tvdbIdToFetch)) return null;
        }
        else if (!source.Equals("tvdb", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var url = $"series/{tvdbIdToFetch}/extended";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.GetProperty("data");

        var result = new MetadataResult
        {
            TvdbId = tvdbIdToFetch,
            Title = data.TryGetProperty("name", out var n) && n.ValueKind != JsonValueKind.Null ? n.GetString() : null,
            Overview = data.TryGetProperty("overview", out var ov) && ov.ValueKind != JsonValueKind.Null ? ov.GetString() : null,
            PosterUrl = data.TryGetProperty("image", out var img) && img.ValueKind != JsonValueKind.Null ? img.GetString() : null,
            Status = data.TryGetProperty("status", out var st) && st.ValueKind != JsonValueKind.Null && st.TryGetProperty("name", out var stn) && stn.ValueKind != JsonValueKind.Null ? stn.GetString() : null,
        };

        if (data.TryGetProperty("contentRatings", out var crList) && crList.ValueKind == JsonValueKind.Array && crList.GetArrayLength() > 0)
        {
            var usRating = crList.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("country", out var cc) && cc.GetString() == "usa");
            if (usRating.ValueKind != JsonValueKind.Undefined && usRating.TryGetProperty("name", out var rating))
            {
                result.ContentRating = rating.GetString();
            }
        }

        if (data.TryGetProperty("artworks", out var artworks) && artworks.ValueKind == JsonValueKind.Array)
        {
            var backdrop = artworks.EnumerateArray()
                .Where(a => a.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number && t.GetInt32() == 3)
                .OrderByDescending(a => a.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0)
                .FirstOrDefault();

            if (backdrop.ValueKind != JsonValueKind.Undefined && backdrop.TryGetProperty("image", out var bImg) && bImg.ValueKind != JsonValueKind.Null)
            {
                result.BackgroundUrl = bImg.GetString();
            }
        }

        var releaseDateStr = data.TryGetProperty("firstAired", out var fa) && fa.ValueKind != JsonValueKind.Null ? fa.GetString() : null;
        if (!string.IsNullOrEmpty(releaseDateStr) && DateTime.TryParse(releaseDateStr, out var d))
        {
            result.ReleaseDate = DateTime.SpecifyKind(d, DateTimeKind.Utc);
        }

        if (data.TryGetProperty("characters", out var chars) && chars.ValueKind == JsonValueKind.Array)
        {
            foreach (var character in chars.EnumerateArray())
            {
                result.Cast.Add(new CastMemberResult
                {
                    TmdbId = 0,

                    Name = character.TryGetProperty("personName", out var pn) && pn.ValueKind != JsonValueKind.Null ? pn.GetString() ?? "Unknown" : "Unknown",
                    CharacterName = character.TryGetProperty("name", out var cn) && cn.ValueKind != JsonValueKind.Null ? cn.GetString() : null,

                    ProfileImageUrl = character.TryGetProperty("personImgURL", out var pimg) && pimg.ValueKind != JsonValueKind.Null ? pimg.GetString() :
                                      (character.TryGetProperty("image", out var cimg) && cimg.ValueKind != JsonValueKind.Null ? cimg.GetString() : null),

                    Roles = MapPeopleTypeToRole(character.TryGetProperty("peopleType", out var pt) && pt.ValueKind != JsonValueKind.Null ? pt.GetString() : null)
                });
            }
        }

        if (data.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genres.EnumerateArray())
            {
                if (genre.TryGetProperty("id", out var gid) && gid.ValueKind == JsonValueKind.Number) result.GenreIds.Add(gid.GetInt32());
            }
        }

        if (data.TryGetProperty("seasons", out var seasons) && seasons.ValueKind == JsonValueKind.Array)
        {
            var seasonDataList = new List<(int Number, int Id, string Name, string? Poster)>();
            foreach (var season in seasons.EnumerateArray())
            {
                if (season.TryGetProperty("type", out var typeObj) && typeObj.TryGetProperty("id", out var typeId) && typeId.GetInt32() != 1) continue;

                var sNumber = season.TryGetProperty("number", out var sn) && sn.ValueKind == JsonValueKind.Number ? sn.GetInt32() : 0;
                var sId = season.TryGetProperty("id", out var sid) && sid.ValueKind == JsonValueKind.Number ? sid.GetInt32() : 0;
                var sNameRaw = season.TryGetProperty("name", out var sname) && sname.ValueKind != JsonValueKind.Null ? sname.GetString() : null;
                var sName = string.IsNullOrEmpty(sNameRaw) ? $"Season {sNumber}" : sNameRaw;
                var sPoster = season.TryGetProperty("image", out var simg) && simg.ValueKind != JsonValueKind.Null ? simg.GetString() : null;

                seasonDataList.Add((sNumber, sId, sName, sPoster));
            }

            var fetchTasks = seasonDataList.Select(async s =>
            {
                var seasonResult = new SeasonResult { Id = s.Id, SeasonNumber = s.Number, Name = s.Name, PosterUrl = s.Poster };

                if (_fetchExtendedSeasonData && s.Id != 0)
                {
                    var seasonUrl = $"seasons/{s.Id}/extended";
                    using var sRequest = new HttpRequestMessage(HttpMethod.Get, seasonUrl);
                    sRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var sResponse = await _httpClient.SendAsync(sRequest);

                    if (sResponse.IsSuccessStatusCode)
                    {
                        using var sDoc = JsonDocument.Parse(await sResponse.Content.ReadAsStringAsync());
                        if (sDoc.RootElement.TryGetProperty("data", out var sData))
                        {
                            if (sData.TryGetProperty("overview", out var sOv) && sOv.ValueKind != JsonValueKind.Null)
                                seasonResult.Overview = sOv.GetString();

                            if (sData.TryGetProperty("score", out var sScore) && sScore.ValueKind == JsonValueKind.Number)
                                seasonResult.VoteAverage = sScore.GetDecimal();

                            if (sData.TryGetProperty("episodes", out var sEps) && sEps.ValueKind == JsonValueKind.Array && sEps.GetArrayLength() > 0)
                            {
                                seasonResult.EpisodeCount = sEps.GetArrayLength();

                                var now = DateTime.UtcNow;

                                foreach (var ep in sEps.EnumerateArray())
                                {
                                    if (ep.TryGetProperty("aired", out var airedProp) && airedProp.ValueKind != JsonValueKind.Null)
                                    {
                                        var airDateStr = airedProp.GetString();
                                        if (!string.IsNullOrEmpty(airDateStr) && DateTime.TryParse(airDateStr, out var sDate))
                                        {
                                            var utcDate = DateTime.SpecifyKind(sDate, DateTimeKind.Utc);

                                            if (seasonResult.AirDate == null) seasonResult.AirDate = utcDate;

                                            if (utcDate >= now.Date)
                                            {
                                                seasonResult.UpcomingEpisodes.Add(new UpcomingEpisodeResult
                                                {
                                                    SeasonNumber = s.Number,
                                                    EpisodeNumber = ep.TryGetProperty("number", out var en) && en.ValueKind == JsonValueKind.Number ? en.GetInt32() : 0,
                                                    Title = ep.TryGetProperty("name", out var ename) && ename.ValueKind != JsonValueKind.Null ? ename.GetString() ?? "TBA" : "TBA",
                                                    AirDate = utcDate
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                return seasonResult;
            });

            var completedSeasons = await Task.WhenAll(fetchTasks);
            result.UpcomingEpisodes.AddRange(completedSeasons.SelectMany(s => s.UpcomingEpisodes));
            result.Seasons.AddRange(completedSeasons);
        }

        if (data.TryGetProperty("networks", out var networks) && networks.ValueKind == JsonValueKind.Array)
        {
            foreach (var net in networks.EnumerateArray())
            {
                result.Networks.Add(new NetworkResult
                {
                    Id = net.TryGetProperty("id", out var nid) && nid.ValueKind == JsonValueKind.Number ? nid.GetInt32() : 0,
                    Name = net.TryGetProperty("name", out var nn) && nn.ValueKind != JsonValueKind.Null ? nn.GetString() ?? "Unknown" : "Unknown"
                });
            }
        }

        return result;
    }

    public async Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default)
    {
        await Task.Delay(250);

        if (!_tvdbEpisodeCache.TryGetValue(showTmdbId, out var showEpisodes))
        {
            showEpisodes = new Dictionary<string, MetadataResult>();
            var token = await GetValidTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                for (int page = 0; page < 5; page++)
                {
                    var url = $"series/{showTmdbId}/episodes/default?page={page}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode) break;

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    var data = doc.RootElement.GetProperty("data");

                    if (data.TryGetProperty("episodes", out var episodes) && episodes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ep in episodes.EnumerateArray())
                        {
                            var sNum = ep.TryGetProperty("seasonNumber", out var sn) && sn.ValueKind == JsonValueKind.Number ? sn.GetInt32() : -1;
                            var eNum = ep.TryGetProperty("number", out var en) && en.ValueKind == JsonValueKind.Number ? en.GetInt32() : -1;

                            if (sNum != -1 && eNum != -1)
                            {
                                showEpisodes[$"{sNum}_{eNum}"] = new MetadataResult
                                {
                                    TvdbId = ep.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : null,
                                    Title = ep.TryGetProperty("name", out var t) && t.ValueKind != JsonValueKind.Null ? t.GetString() : null,
                                    Overview = ep.TryGetProperty("overview", out var ov) && ov.ValueKind != JsonValueKind.Null ? ov.GetString() : null,
                                    ReleaseDate = DateTime.TryParse(ep.TryGetProperty("aired", out var rd) && rd.ValueKind != JsonValueKind.Null ? rd.GetString() : "", out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null,
                                    PosterUrl = ep.TryGetProperty("image", out var img) && img.ValueKind != JsonValueKind.Null ? img.GetString() : null,
                                    BackgroundUrl = ep.TryGetProperty("image", out var bImg) && bImg.ValueKind != JsonValueKind.Null ? bImg.GetString() : null,
                                    RuntimeMinutes = ep.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt32() : null,
                                    Rating = ep.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? (decimal)sc.GetDouble() : null
                                };
                            }
                        }
                    }

                    if (doc.RootElement.TryGetProperty("links", out var links) && links.ValueKind != JsonValueKind.Null &&
                        links.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.Null)
                    {
                        break; // No more pages left!
                    }
                }
            }

            _tvdbEpisodeCache[showTmdbId] = showEpisodes;
        }

        if (showEpisodes.TryGetValue($"{seasonNumber}_{episodeNumber}", out var result))
        {
            return result;
        }

        return null;
    }

    public async Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId, CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        var url = $"people/{personId}/extended";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.GetProperty("data");

        string? biographyStr = null;
        if (data.TryGetProperty("biographies", out var bios) && bios.ValueKind == JsonValueKind.Array && bios.GetArrayLength() > 0)
        {
            var firstBio = bios[0];
            biographyStr = firstBio.TryGetProperty("biography", out var bText) && bText.ValueKind != JsonValueKind.Null ? bText.GetString() : null;
        }

        return new ActorMetadataResult
        {
            Biography = biographyStr,
            PlaceOfBirth = data.TryGetProperty("birthPlace", out var pb) && pb.ValueKind != JsonValueKind.Null ? pb.GetString() : null,
            Birthday = DateTime.TryParse(data.TryGetProperty("birth", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : "", out var bd) ? DateTime.SpecifyKind(bd, DateTimeKind.Utc) : null,
            Deathday = DateTime.TryParse(data.TryGetProperty("death", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : "", out var dd) ? DateTime.SpecifyKind(dd, DateTimeKind.Utc) : null
        };
    }

    private static CastRole MapPeopleTypeToRole(string? peopleType) => peopleType switch
    {
        "Director" => CastRole.Director,
        "Writer" => CastRole.Writer,
        "Producer" => CastRole.Producer,
        "Creator" => CastRole.Creator,
        _ => CastRole.Actor
    };
}
