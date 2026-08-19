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
    private readonly Dictionary<string, SeasonPosterInfo> _seasonPosterCache = new();
    private string? _cachedLanguage;

    private sealed class SeasonPosterInfo
    {
        public Dictionary<int, string> PosterByNumber { get; init; } = new();
        public string? ShowPoster { get; init; }
    }

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
                Required = true,
                Placeholder = "Paste your TVDB API key",
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

    // The server-wide metadata language as a TVDB 3-letter code (e.g. "eng").
    // Cached for the lifetime of this (transient) provider instance so a single
    // show + its episodes don't each re-read the setting.
    private async Task<string> GetLanguageAsync()
    {
        if (_cachedLanguage != null) return _cachedLanguage;
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var lang = await settings.GetMetadataLanguageAsync();
        _cachedLanguage = string.IsNullOrWhiteSpace(lang) ? "eng" : lang.Trim();
        return _cachedLanguage;
    }

    // TVDB's series/season/movie endpoints return absolute artwork URLs, but the
    // episodes endpoint returns a relative path (e.g. "/banners/v4/episode/…").
    // Stored raw, the client resolves it against the Vora host and the image
    // 404s — so an episode still needs the artwork host prefixed. Idempotent for
    // values that are already absolute.
    private const string TvdbArtworkBaseUrl = "https://artworks.thetvdb.com";
    private static string? PrefixArtwork(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
        return path.StartsWith("/", StringComparison.Ordinal)
            ? TvdbArtworkBaseUrl + path
            : $"{TvdbArtworkBaseUrl}/{path}";
    }

    // TVDB's extended series/movie carries the title's external ids in remoteIds
    // (IMDB, TheMovieDB.com, …). Backfill them onto the result so a title-matched
    // item gets its imdb/tmdb id saved — and a wrong folder imdb id (e.g. Only
    // Murders' folder tag pointing at the wrong title) is corrected to TVDB's.
    // Best per-season-NUMBER poster from the series artworks (type 7), preferring
    // the configured language, then a language-neutral one. Season-poster artworks
    // are keyed to the "official" order season id, so resolve every season entry's
    // id -> number first (the aired-order seasons we use share those numbers).
    private static Dictionary<int, string> BuildSeasonPosterMap(JsonElement seasons, JsonElement data, string lang)
    {
        var result = new Dictionary<int, string>();
        var idToNumber = new Dictionary<int, int>();
        foreach (var s in seasons.EnumerateArray())
        {
            if (s.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                && s.TryGetProperty("number", out var num) && num.ValueKind == JsonValueKind.Number)
            {
                idToNumber[id.GetInt32()] = num.GetInt32();
            }
        }

        if (!data.TryGetProperty("artworks", out var arts) || arts.ValueKind != JsonValueKind.Array) return result;

        var best = new Dictionary<int, (int Rank, double Score)>();
        foreach (var a in arts.EnumerateArray())
        {
            if (!a.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.Number || t.GetInt32() != 7) continue;
            if (!a.TryGetProperty("seasonId", out var sid) || sid.ValueKind != JsonValueKind.Number) continue;
            if (!idToNumber.TryGetValue(sid.GetInt32(), out var number)) continue;
            var img = a.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.String ? im.GetString() : null;
            if (string.IsNullOrEmpty(img)) continue;

            var aLang = a.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
            var rank = string.Equals(aLang, lang, StringComparison.OrdinalIgnoreCase) ? 2 : (string.IsNullOrEmpty(aLang) ? 1 : 0);
            if (rank == 0) continue; // skip posters in other languages

            var score = a.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0;
            if (!best.TryGetValue(number, out var cur) || rank > cur.Rank || (rank == cur.Rank && score > cur.Score))
            {
                best[number] = (rank, score);
                result[number] = PrefixArtwork(img)!;
            }
        }
        return result;
    }

    private static void ApplyRemoteIds(JsonElement data, MetadataResult result)
    {
        if (!data.TryGetProperty("remoteIds", out var rids) || rids.ValueKind != JsonValueKind.Array) return;
        foreach (var rid in rids.EnumerateArray())
        {
            var source = rid.TryGetProperty("sourceName", out var s) ? s.GetString() : null;
            var id = rid.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (string.Equals(source, "IMDB", StringComparison.OrdinalIgnoreCase))
                result.ImdbId = id;
            else if (string.Equals(source, "TheMovieDB.com", StringComparison.OrdinalIgnoreCase))
                result.TmdbId = id;
        }
    }

    public async Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteSearch(query, "movie", year);
        // TVDB's year filter is exact, but a local folder's year is often off by
        // one (release vs. air year, or just wrong) — so a year-filtered miss
        // retries without the year rather than leaving the title unmatched.
        if (result == null && year.HasValue)
        {
            result = await ExecuteSearch(query, "movie", null);
        }
        return result;
    }

    public async Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        // A plain search result carries no seasons array. Once it gives us a TVDB
        // id, re-fetch the extended series (which includes seasons + their poster
        // images) so title-matched shows get season posters too — not just shows
        // matched by external id.
        var searchResult = await ExecuteSearch(query, "series", year);
        // Exact-year miss → retry without the year (folder years are frequently
        // off by one, which otherwise leaves the show completely unmatched).
        if (string.IsNullOrEmpty(searchResult?.TvdbId) && year.HasValue)
        {
            searchResult = await ExecuteSearch(query, "series", null);
        }
        if (!string.IsNullOrEmpty(searchResult?.TvdbId))
        {
            var extended = await FetchTvShowMetadataByIdAsync(searchResult.TvdbId, "tvdb", cancellationToken);
            if (extended != null) return extended;
        }
        return searchResult;
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
            // Resolve the IMDB id via the remote-id endpoint (see the TV method) —
            // `search?query=` does not match an IMDB id string.
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, $"search/remoteid/{Uri.EscapeDataString(id)}");
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var searchRes = await _httpClient.SendAsync(searchReq);
            if (!searchRes.IsSuccessStatusCode) return null;

            using var searchStream = await searchRes.Content.ReadAsStreamAsync();
            using var searchDoc = await JsonDocument.ParseAsync(searchStream);
            // remoteid returns {"status":"success","data":null} for an unknown id —
            // "data" exists but is null, so GetArrayLength() would throw and abort
            // the whole fetch (skipping the title-search fallback). Guard on Array.
            if (!searchDoc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array || dataArr.GetArrayLength() == 0) return null;

            if (!dataArr[0].TryGetProperty("movie", out var movieObj) || !movieObj.TryGetProperty("id", out var movieId)) return null;
            tvdbIdToFetch = movieId.ValueKind == JsonValueKind.Number ? movieId.GetInt32().ToString() : movieId.GetString() ?? "";

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

        ApplyRemoteIds(data, result);
        await ApplyTranslationAsync(tvdbIdToFetch, "movies", data, result, token);

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

    // Resolve a single season's poster from the parent series' extended data.
    // Season rows created after the show's ProcessTvSeasons pass (folder-watcher
    // new seasons, partial scans) never received a poster; the per-season refresh
    // used to only stamp a timestamp. This gives that refresh a real poster —
    // the configured-language season poster (type 7), falling back to the show
    // poster so a season is never left blank. The per-instance cache means the
    // series' extended is fetched once even when several sibling seasons refresh.
    public async Task<MetadataResult?> FetchSeasonMetadataAsync(string showId, string source, int seasonNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(showId) || !source.Equals("tvdb", StringComparison.OrdinalIgnoreCase)) return null;

        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        if (!_seasonPosterCache.TryGetValue(showId, out var info))
        {
            info = await LoadSeasonPosterInfoAsync(showId, token);
            _seasonPosterCache[showId] = info;
        }

        var poster = info.PosterByNumber.TryGetValue(seasonNumber, out var chosen) ? chosen : info.ShowPoster;
        if (string.IsNullOrEmpty(poster)) return null;

        return new MetadataResult { TvdbId = showId, PosterUrl = poster };
    }

    private async Task<SeasonPosterInfo> LoadSeasonPosterInfoAsync(string tvdbId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"series/{tvdbId}/extended");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new SeasonPosterInfo();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var data)) return new SeasonPosterInfo();

        var showPoster = data.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.String ? im.GetString() : null;

        var posters = data.TryGetProperty("seasons", out var seasons) && seasons.ValueKind == JsonValueKind.Array
            ? BuildSeasonPosterMap(seasons, data, await GetLanguageAsync())
            : new Dictionary<int, string>();

        return new SeasonPosterInfo { PosterByNumber = posters, ShowPoster = showPoster };
    }

    public async Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        string tvdbIdToFetch = id;

        if (source.Equals("imdb", StringComparison.OrdinalIgnoreCase))
        {
            // Resolve the external (IMDB) id to a TVDB series id via the remote-id
            // endpoint. The generic `search?query=` endpoint does NOT match an
            // IMDB id string, so an imdb-tagged show would otherwise fail here and
            // fall back to a title search that never fetches the extended data
            // (and its seasons), leaving season posters blank.
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, $"search/remoteid/{Uri.EscapeDataString(id)}");
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var searchRes = await _httpClient.SendAsync(searchReq);
            if (!searchRes.IsSuccessStatusCode) return null;

            using var searchStream = await searchRes.Content.ReadAsStreamAsync();
            using var searchDoc = await JsonDocument.ParseAsync(searchStream);
            // remoteid returns {"status":"success","data":null} for an unknown id —
            // "data" exists but is null, so GetArrayLength() would throw and abort
            // the whole fetch (skipping the title-search fallback). Guard on Array.
            if (!searchDoc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array || dataArr.GetArrayLength() == 0) return null;

            if (!dataArr[0].TryGetProperty("series", out var seriesObj) || !seriesObj.TryGetProperty("id", out var seriesId)) return null;
            tvdbIdToFetch = seriesId.ValueKind == JsonValueKind.Number ? seriesId.GetInt32().ToString() : seriesId.GetString() ?? "";

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

        ApplyRemoteIds(data, result);
        await ApplyTranslationAsync(tvdbIdToFetch, "series", data, result, token);

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
            var seriesOriginalLang = data.TryGetProperty("originalLanguage", out var slo) && slo.ValueKind == JsonValueKind.String ? slo.GetString() : null;

            // TVDB's per-season "image" is whatever it picked as default — often a
            // foreign-language poster, or null. The series' artworks list carries
            // proper per-language season posters (type 7). Prefer the configured
            // language, then a language-neutral one, keyed by season number.
            var seasonPosterByNumber = BuildSeasonPosterMap(seasons, data, await GetLanguageAsync());

            var seasonDataList = new List<(int Number, int Id, string Name, string? Poster)>();
            foreach (var season in seasons.EnumerateArray())
            {
                if (season.TryGetProperty("type", out var typeObj) && typeObj.TryGetProperty("id", out var typeId) && typeId.GetInt32() != 1) continue;

                var sNumber = season.TryGetProperty("number", out var sn) && sn.ValueKind == JsonValueKind.Number ? sn.GetInt32() : 0;
                var sId = season.TryGetProperty("id", out var sid) && sid.ValueKind == JsonValueKind.Number ? sid.GetInt32() : 0;
                var sNameRaw = season.TryGetProperty("name", out var sname) && sname.ValueKind != JsonValueKind.Null ? sname.GetString() : null;
                var sName = string.IsNullOrEmpty(sNameRaw) ? $"Season {sNumber}" : sNameRaw;
                var defaultPoster = PrefixArtwork(season.TryGetProperty("image", out var simg) && simg.ValueKind != JsonValueKind.Null ? simg.GetString() : null);
                // Language-appropriate artwork → TVDB default → show poster.
                var sPoster = seasonPosterByNumber.TryGetValue(sNumber, out var chosen) ? chosen : (defaultPoster ?? result.PosterUrl);

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

                            await ApplySeasonOverviewTranslationAsync(s.Id, sData, seasonResult, seriesOriginalLang, token);

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
        if (!_tvdbEpisodeCache.TryGetValue(showTmdbId, out var showEpisodes))
        {
            // Only pace the actual network fetch — every episode of a show hits
            // this method, but the list is fetched once and the rest are cache
            // hits, so delaying on a hit needlessly slowed episode enrichment.
            await Task.Delay(250);

            showEpisodes = new Dictionary<string, MetadataResult>();
            var token = await GetValidTokenAsync();
            var lang = await GetLanguageAsync();

            if (!string.IsNullOrEmpty(token))
            {
                for (int page = 0; page < 5; page++)
                {
                    var url = $"series/{showTmdbId}/episodes/default/{lang}?page={page}";
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
                                    PosterUrl = PrefixArtwork(ep.TryGetProperty("image", out var img) && img.ValueKind != JsonValueKind.Null ? img.GetString() : null),
                                    BackgroundUrl = PrefixArtwork(ep.TryGetProperty("image", out var bImg) && bImg.ValueKind != JsonValueKind.Null ? bImg.GetString() : null),
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

    private async Task ApplySeasonOverviewTranslationAsync(int seasonId, JsonElement sData, SeasonResult seasonResult, string? originalLang, string token)
    {
        var lang = await GetLanguageAsync();

        // The season-extended overview is in the series' original language, so skip
        // the extra call when that already matches the configured language.
        if (string.Equals(originalLang, lang, StringComparison.OrdinalIgnoreCase)) return;
        if (!sData.TryGetProperty("overviewTranslations", out var ot) || ot.ValueKind != JsonValueKind.Array) return;
        if (!ot.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == lang)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"seasons/{seasonId}/translations/{lang}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("data", out var tr) || tr.ValueKind != JsonValueKind.Object) return;

            if (tr.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ov.GetString()))
            {
                seasonResult.Overview = ov.GetString();
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }

    private async Task ApplyTranslationAsync(string tvdbId, string kind, JsonElement data, MetadataResult result, string token)
    {
        var lang = await GetLanguageAsync();

        // The extended name/overview are already in the title's original language,
        // so when that IS the configured language there's nothing to fetch — this
        // skips the extra call for (e.g.) English titles on an English server.
        if (data.TryGetProperty("originalLanguage", out var ol) && ol.ValueKind == JsonValueKind.String &&
            string.Equals(ol.GetString(), lang, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Only override when the title actually advertises a translation in the
        // configured language.
        if (!data.TryGetProperty("nameTranslations", out var nt) || nt.ValueKind != JsonValueKind.Array) return;
        if (!nt.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == lang)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{kind}/{tvdbId}/translations/{lang}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("data", out var tr) || tr.ValueKind != JsonValueKind.Object) return;

            if (tr.TryGetProperty("name", out var en) && en.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(en.GetString()))
            {
                result.Title = en.GetString();
            }
            if (tr.TryGetProperty("overview", out var eo) && eo.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(eo.GetString()))
            {
                result.Overview = eo.GetString();
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
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
