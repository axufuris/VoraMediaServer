using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tmdb;

public class TmdbMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, Dictionary<int, MetadataResult>> _tmdbSeasonCache = new();
    private string? _cachedLanguage;

    public string Id => "tmdb_metadata";
    public string Name => "The Movie Database (TMDB)";
    public string Version => "1.0.0";
    public string Description => "Official Vora metadata agent for fetching movies, TV shows, and actor details from TMDB.";
    public bool IsSystemPlugin => true;
    public string Type => "Metadata";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ProviderName => "TMDB";

    public TmdbMetadataProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "TMDB API Key",
                Type = "password",
                Required = true,
                Placeholder = "Paste your TMDB API key",
                Description = "Free TMDB v3 API key. Sign up at https://www.themoviedb.org/signup, then request a key under Settings → API (https://www.themoviedb.org/settings/api) and click 'Create'. Copy the 'API Key (v3 auth)' value — not the Read Access Token."
            }
        };
    }

    private async Task<string?> GetApiKeyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        return await settings.GetSettingAsync(Id, "api_key");
    }

    // TMDB ISO 639-1 code for the server's metadata language, cached for this
    // (transient) provider instance.
    private async Task<string> GetLanguageAsync()
    {
        if (_cachedLanguage != null) return _cachedLanguage;
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var stored = await settings.GetMetadataLanguageAsync();
        _cachedLanguage = MetadataLanguageCodes.ToIso6391(stored);
        return _cachedLanguage;
    }

    public async Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        var lang = await GetLanguageAsync();
        if (string.IsNullOrEmpty(apiKey)) return null;

        var url = $"search/movie?api_key={apiKey}&language={lang}&query={Uri.EscapeDataString(query)}";
        if (year.HasValue) url += $"&year={year.Value}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var results = document.RootElement.GetProperty("results");

        if (results.GetArrayLength() == 0) return null;

        var tmdbId = results[0].GetProperty("id").GetInt32().ToString();
        return await FetchMovieMetadataByIdAsync(tmdbId, "tmdb");
    }

    public async Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        var lang = await GetLanguageAsync();
        if (string.IsNullOrEmpty(apiKey)) return null;

        var url = $"search/tv?api_key={apiKey}&language={lang}&query={Uri.EscapeDataString(query)}";
        if (year.HasValue) url += $"&first_air_date_year={year.Value}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var results = document.RootElement.GetProperty("results");

        if (results.GetArrayLength() == 0) return null;

        var tmdbId = results[0].GetProperty("id").GetInt32().ToString();
        return await FetchTvShowMetadataByIdAsync(tmdbId, "tmdb");
    }

    public async Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        var lang = await GetLanguageAsync();
        string tmdbIdToFetch = id;

        if (source.ToLower() == "imdb")
        {
            var findResponse = await _httpClient.GetAsync($"find/{id}?api_key={apiKey}&external_source=imdb_id");
            if (!findResponse.IsSuccessStatusCode) return null;
            using var findStream = await findResponse.Content.ReadAsStreamAsync();
            using var findDoc = await JsonDocument.ParseAsync(findStream);
            var movieResults = findDoc.RootElement.GetProperty("movie_results");
            if (movieResults.GetArrayLength() == 0) return null;
            tmdbIdToFetch = movieResults[0].GetProperty("id").GetInt32().ToString();
        }

        var url = $"movie/{tmdbIdToFetch}?api_key={apiKey}&language={lang}&append_to_response=credits,external_ids,release_dates,videos,keywords";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return MapResult(doc.RootElement, isTv: false);
    }

    public async Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        var lang = await GetLanguageAsync();
        string tmdbIdToFetch = id;

        if (source.ToLower() == "imdb")
        {
            var findResponse = await _httpClient.GetAsync($"find/{id}?api_key={apiKey}&external_source=imdb_id");
            if (!findResponse.IsSuccessStatusCode) return null;
            using var findStream = await findResponse.Content.ReadAsStreamAsync();
            using var findDoc = await JsonDocument.ParseAsync(findStream);
            var tvResults = findDoc.RootElement.GetProperty("tv_results");
            if (tvResults.GetArrayLength() == 0) return null;
            tmdbIdToFetch = tvResults[0].GetProperty("id").GetInt32().ToString();
        }

        var url = $"tv/{tmdbIdToFetch}?api_key={apiKey}&language={lang}&append_to_response=credits,external_ids,content_ratings,videos";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return MapResult(doc.RootElement, isTv: true);
    }

    private MetadataResult MapResult(JsonElement el, bool isTv)
    {
        var result = new MetadataResult
        {
            TmdbId = el.TryGetProperty("id", out var idProp) ? idProp.GetInt32().ToString() : null,
            Title = el.TryGetProperty(isTv ? "name" : "title", out var t) ? t.GetString() : null,
            OriginalTitle = el.TryGetProperty(isTv ? "original_name" : "original_title", out var ot) ? ot.GetString() : null,
            Overview = el.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
            ReleaseDate = DateTime.TryParse(el.TryGetProperty(isTv ? "first_air_date" : "release_date", out var rd) ? rd.GetString() : "", out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            PosterUrl = el.TryGetProperty("poster_path", out var p) && p.GetString() != null ? $"https://image.tmdb.org/t/p/w500{p.GetString()}" : null,
            BackgroundUrl = el.TryGetProperty("backdrop_path", out var b) && b.GetString() != null ? $"https://image.tmdb.org/t/p/w1280{b.GetString()}" : null,
            IsAdult = el.TryGetProperty("adult", out var ad) && ad.GetBoolean(),
            OriginalLanguage = el.TryGetProperty("original_language", out var ol) ? ol.GetString() : null,
            Rating = el.TryGetProperty("vote_average", out var va) && va.TryGetDecimal(out var rating) ? rating : null,

            Status = el.TryGetProperty("status", out var st) ? st.GetString() : null,
            Tagline = el.TryGetProperty("tagline", out var tag) ? tag.GetString() : null,
            HomePage = el.TryGetProperty("homepage", out var hp) ? hp.GetString() : null,
            Budget = el.TryGetProperty("budget", out var bgt) ? bgt.GetInt64() : null,
            Revenue = el.TryGetProperty("revenue", out var rev) ? rev.GetInt64() : null,
            RuntimeMinutes = el.TryGetProperty("runtime", out var rt) ? rt.GetInt32() : null
        };

        if (!isTv && el.TryGetProperty("release_dates", out var rdObj) && rdObj.TryGetProperty("results", out var rdResults))
        {
            foreach (var country in rdResults.EnumerateArray())
            {
                if (country.TryGetProperty("iso_3166_1", out var iso) && iso.GetString() == "US")
                {
                    if (country.TryGetProperty("release_dates", out var dates) && dates.ValueKind == JsonValueKind.Array)
                    {
                        // A US movie has several release entries (premiere,
                        // theatrical, digital, physical…) and only some carry a
                        // certification — the first is often blank. Scan them all
                        // for the first non-empty cert instead of trusting [0].
                        foreach (var usRelease in dates.EnumerateArray())
                        {
                            var cert = usRelease.TryGetProperty("certification", out var c) ? c.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(cert))
                            {
                                result.ContentRating = cert;
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(result.ContentRating)) break;
                }
            }
        }

        if (isTv && el.TryGetProperty("content_ratings", out var crObj) && crObj.TryGetProperty("results", out var crResults))
        {
            foreach (var country in crResults.EnumerateArray())
            {
                if (country.TryGetProperty("iso_3166_1", out var iso) && iso.GetString() == "US")
                {
                    var cert = country.TryGetProperty("rating", out var r) ? r.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(cert))
                    {
                        result.ContentRating = cert;
                        break;
                    }
                }
            }
        }

        if (el.TryGetProperty("external_ids", out var extIds) && extIds.TryGetProperty("imdb_id", out var iId) && iId.ValueKind != JsonValueKind.Null)
        {
            result.ImdbId = iId.GetString();
        }
        else if (el.TryGetProperty("imdb_id", out var rootImdb) && rootImdb.ValueKind != JsonValueKind.Null)
        {
            result.ImdbId = rootImdb.GetString();
        }

        // TMDB cross-references TV shows to TVDB (movies aren't), so this fills in
        // a show's TvdbId for free from data we already fetched — no extra call.
        if (el.TryGetProperty("external_ids", out var extIdsForTvdb)
            && extIdsForTvdb.TryGetProperty("tvdb_id", out var tvdbId)
            && tvdbId.ValueKind == JsonValueKind.Number)
        {
            result.TvdbId = tvdbId.GetInt32().ToString();
        }

        if (isTv && el.TryGetProperty("episode_run_time", out var runTimes) && runTimes.GetArrayLength() > 0)
        {
            result.RuntimeMinutes = runTimes[0].GetInt32();
        }

        if (isTv)
        {
            result.InProduction = el.TryGetProperty("in_production", out var ip) ? ip.GetBoolean() : null;
            result.TvType = el.TryGetProperty("type", out var type) ? type.GetString() : null;
            result.NumberOfEpisodes = el.TryGetProperty("number_of_episodes", out var noe) ? noe.GetInt32() : null;
            result.NumberOfSeasons = el.TryGetProperty("number_of_seasons", out var nos) ? nos.GetInt32() : null;

            result.LastAirDate = DateTime.TryParse(el.TryGetProperty("last_air_date", out var lad) ? lad.GetString() : "", out var lDate)
                ? DateTime.SpecifyKind(lDate, DateTimeKind.Utc) : null;

            if (el.TryGetProperty("last_episode_to_air", out var le) && le.ValueKind != JsonValueKind.Null)
                result.LastEpisodeToAirName = le.TryGetProperty("name", out var len) ? len.GetString() : null;

            if (el.TryGetProperty("next_episode_to_air", out var ne) && ne.ValueKind != JsonValueKind.Null)
            {
                result.NextEpisodeToAirName = ne.TryGetProperty("name", out var nen) ? nen.GetString() : null;
                var nextAirDateStr = ne.TryGetProperty("air_date", out var nda) ? nda.GetString() : "";

                if (DateTime.TryParse(nextAirDateStr, out var nDate))
                {
                    result.NextAirDate = DateTime.SpecifyKind(nDate, DateTimeKind.Utc);

                    result.UpcomingEpisodes.Add(new UpcomingEpisodeResult
                    {
                        SeasonNumber = ne.TryGetProperty("season_number", out var sn) ? sn.GetInt32() : 0,
                        EpisodeNumber = ne.TryGetProperty("episode_number", out var en) ? en.GetInt32() : 0,
                        Title = result.NextEpisodeToAirName ?? "TBA",
                        AirDate = result.NextAirDate.Value
                    });
                }
            }

            if (el.TryGetProperty("networks", out var nets) && nets.ValueKind == JsonValueKind.Array)
            {
                foreach (var net in nets.EnumerateArray())
                {
                    result.Networks.Add(new NetworkResult
                    {
                        Id = net.GetProperty("id").GetInt32(),
                        Name = net.GetProperty("name").GetString() ?? "Unknown",
                        LogoPath = net.TryGetProperty("logo_path", out var lp) && lp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{lp.GetString()}" : null,
                        OriginCountry = net.TryGetProperty("origin_country", out var oc) ? oc.GetString() : null
                    });
                }
            }

            if (el.TryGetProperty("seasons", out var seasons) && seasons.ValueKind == JsonValueKind.Array)
            {
                foreach (var season in seasons.EnumerateArray())
                {
                    result.Seasons.Add(new SeasonResult
                    {
                        Id = season.GetProperty("id").GetInt32(),
                        SeasonNumber = season.GetProperty("season_number").GetInt32(),
                        Name = season.GetProperty("name").GetString() ?? "Unknown",
                        Overview = season.TryGetProperty("overview", out var sov) ? sov.GetString() : null,
                        PosterUrl = season.TryGetProperty("poster_path", out var sp) && sp.GetString() != null ? $"https://image.tmdb.org/t/p/w500{sp.GetString()}" : null,
                        AirDate = DateTime.TryParse(season.TryGetProperty("air_date", out var sad) ? sad.GetString() : "", out var sDate) ? DateTime.SpecifyKind(sDate, DateTimeKind.Utc) : null,
                        EpisodeCount = season.TryGetProperty("episode_count", out var sec) ? sec.GetInt32() : 0,
                        VoteAverage = season.TryGetProperty("vote_average", out var sva) && sva.TryGetDecimal(out var sr) ? sr : null
                    });
                }
            }
        }

        if (el.TryGetProperty("belongs_to_collection", out var col) && col.ValueKind != JsonValueKind.Null)
        {
            result.Collection = new CollectionResult
            {
                Id = col.GetProperty("id").GetInt32(),
                Name = col.GetProperty("name").GetString() ?? "Unknown Collection",
                PosterUrl = col.TryGetProperty("poster_path", out var cp) && cp.GetString() != null ? $"https://image.tmdb.org/t/p/w500{cp.GetString()}" : null,
                BackdropUrl = col.TryGetProperty("backdrop_path", out var cb) && cb.GetString() != null ? $"https://image.tmdb.org/t/p/w1280{cb.GetString()}" : null
            };
        }

        if (el.TryGetProperty("production_companies", out var comps) && comps.ValueKind == JsonValueKind.Array)
        {
            foreach (var comp in comps.EnumerateArray())
            {
                result.ProductionCompanies.Add(new CompanyResult
                {
                    Id = comp.GetProperty("id").GetInt32(),
                    Name = comp.GetProperty("name").GetString() ?? "Unknown",
                    LogoPath = comp.TryGetProperty("logo_path", out var lp) && lp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{lp.GetString()}" : null,
                    OriginCountry = comp.TryGetProperty("origin_country", out var oc) ? oc.GetString() : null
                });
            }
        }

        if (el.TryGetProperty("production_countries", out var ctries) && ctries.ValueKind == JsonValueKind.Array)
        {
            foreach (var ctry in ctries.EnumerateArray())
            {
                result.OriginCountries.Add(new CountryResult
                {
                    IsoCode = ctry.GetProperty("iso_3166_1").GetString() ?? "UNKNOWN",
                    Name = ctry.GetProperty("name").GetString() ?? "Unknown"
                });
            }
        }

        if (el.TryGetProperty("genres", out var fullGenres) && fullGenres.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in fullGenres.EnumerateArray())
            {
                if (g.TryGetProperty("id", out var gidProp) && gidProp.TryGetInt32(out int gid)) result.GenreIds.Add(gid);
            }
        }

        var crewDict = new Dictionary<int, CastMemberResult>();

        if (el.TryGetProperty("credits", out var credits))
        {
            if (credits.TryGetProperty("cast", out var castArray))
            {
                foreach (var castMember in castArray.EnumerateArray().Take(50))
                {
                    var tmdbId = castMember.GetProperty("id").GetInt32();
                    crewDict[tmdbId] = new CastMemberResult
                    {
                        TmdbId = tmdbId,
                        Name = castMember.GetProperty("name").GetString() ?? "Unknown",
                        CharacterName = castMember.TryGetProperty("character", out var c) ? c.GetString() : null,
                        ProfileImageUrl = castMember.TryGetProperty("profile_path", out var pp) && pp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null,
                        Roles = CastRole.Actor
                    };
                }
            }

            if (credits.TryGetProperty("crew", out var crewArray))
            {
                foreach (var crewMember in crewArray.EnumerateArray())
                {
                    var department = crewMember.TryGetProperty("department", out var dept) ? dept.GetString() : "";
                    var crewRole = MapDepartmentToRole(department);
                    if (crewRole == CastRole.None) continue;

                    var tmdbId = crewMember.GetProperty("id").GetInt32();

                    if (crewDict.TryGetValue(tmdbId, out var existing))
                    {
                        existing.Roles |= crewRole;
                    }
                    else
                    {
                        crewDict[tmdbId] = new CastMemberResult
                        {
                            TmdbId = tmdbId,
                            Name = crewMember.GetProperty("name").GetString() ?? "Unknown",
                            ProfileImageUrl = crewMember.TryGetProperty("profile_path", out var pp) && pp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null,
                            Roles = crewRole
                        };
                    }
                }
            }
        }

        if (isTv && el.TryGetProperty("created_by", out var creators) && creators.ValueKind == JsonValueKind.Array)
        {
            foreach (var creator in creators.EnumerateArray())
            {
                var tmdbId = creator.GetProperty("id").GetInt32();
                if (crewDict.TryGetValue(tmdbId, out var existing))
                {
                    existing.Roles |= CastRole.Creator;
                }
                else
                {
                    crewDict[tmdbId] = new CastMemberResult
                    {
                        TmdbId = tmdbId,
                        Name = creator.GetProperty("name").GetString() ?? "Unknown",
                        ProfileImageUrl = creator.TryGetProperty("profile_path", out var pp) && pp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null,
                        Roles = CastRole.Creator
                    };
                }
            }
        }

        result.Cast = crewDict.Values.ToList();

        if (el.TryGetProperty("videos", out var vids) && vids.TryGetProperty("results", out var vidArray))
        {
            foreach (var vid in vidArray.EnumerateArray())
            {
                var site = vid.TryGetProperty("site", out var s) ? s.GetString() : "";
                if (site?.Equals("YouTube", StringComparison.OrdinalIgnoreCase) == true)
                {
                    result.Videos.Add(new VideoResult
                    {
                        Key = vid.GetProperty("key").GetString() ?? "",
                        Name = vid.GetProperty("name").GetString() ?? "Unknown Video",
                        Site = site,
                        Type = vid.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                        IsOfficial = vid.TryGetProperty("official", out var off) && off.GetBoolean()
                    });
                }
            }
        }

        if (el.TryGetProperty("keywords", out var keywordsObj) && keywordsObj.TryGetProperty("keywords", out var kwArray))
        {
            foreach (var kw in kwArray.EnumerateArray())
            {
                var kwName = kw.TryGetProperty("name", out var n) ? n.GetString()?.ToLower().Replace(" ", "") : "";
                var kwId = kw.TryGetProperty("id", out var id) ? id.GetInt32() : 0;

                if (kwId == 179431 || kwName == "duringcreditsstinger") result.HasMidCreditsStinger = true;
                if (kwId == 179430 || kwName == "aftercreditsstinger") result.HasPostCreditsStinger = true;
            }
        }

        return result;
    }

    public async Task<MetadataResult?> FetchSeasonMetadataAsync(string showId, string source, int seasonNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(showId) || !source.Equals("tmdb", StringComparison.OrdinalIgnoreCase)) return null;

        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrEmpty(apiKey)) return null;
        var lang = await GetLanguageAsync();

        var response = await _httpClient.GetAsync($"tv/{showId}/season/{seasonNumber}?api_key={apiKey}&language={lang}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var el = doc.RootElement;

        var poster = el.TryGetProperty("poster_path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() != null
            ? $"https://image.tmdb.org/t/p/w500{p.GetString()}" : null;
        var overview = el.TryGetProperty("overview", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
        if (string.IsNullOrEmpty(poster) && string.IsNullOrEmpty(overview)) return null;

        return new MetadataResult { TmdbId = showId, PosterUrl = poster, Overview = string.IsNullOrEmpty(overview) ? null : overview };
    }

    public async Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default)
    {
        await Task.Delay(250);
        var cacheKey = $"{showTmdbId}_{seasonNumber}";

        if (!_tmdbSeasonCache.TryGetValue(cacheKey, out var seasonEpisodes))
        {
            seasonEpisodes = new Dictionary<int, MetadataResult>();
            var apiKey = await GetApiKeyAsync();
            var lang = await GetLanguageAsync();
            var url = $"tv/{showTmdbId}/season/{seasonNumber}?api_key={apiKey}&language={lang}&append_to_response=credits,videos";

            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var el = doc.RootElement;

                if (el.TryGetProperty("episodes", out var episodes) && episodes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ep in episodes.EnumerateArray())
                    {
                        var eNum = ep.TryGetProperty("episode_number", out var en) ? en.GetInt32() : -1;
                        if (eNum == -1) continue;

                        var result = new MetadataResult
                        {
                            TmdbId = ep.TryGetProperty("id", out var idProp) ? idProp.GetInt32().ToString() : null,
                            Title = ep.TryGetProperty("name", out var t) ? t.GetString() : null,
                            Overview = ep.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
                            ReleaseDate = DateTime.TryParse(ep.TryGetProperty("air_date", out var rd) ? rd.GetString() : "", out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
                            Rating = ep.TryGetProperty("vote_average", out var va) && va.TryGetDecimal(out var rating) ? rating : null,
                            RuntimeMinutes = ep.TryGetProperty("runtime", out var rt) ? rt.GetInt32() : null
                        };

                        if (ep.TryGetProperty("still_path", out var p) && p.GetString() != null)
                        {
                            var path = p.GetString();
                            result.BackgroundUrl = $"https://image.tmdb.org/t/p/w1280{path}";
                            result.PosterUrl = $"https://image.tmdb.org/t/p/w500{path}";
                        }

                        var crewDict = new Dictionary<int, CastMemberResult>();

                        if (ep.TryGetProperty("guest_stars", out var guestArray) && guestArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var castMember in guestArray.EnumerateArray())
                            {
                                var tmdbId = castMember.GetProperty("id").GetInt32();
                                crewDict[tmdbId] = new CastMemberResult
                                {
                                    TmdbId = tmdbId,
                                    Name = castMember.GetProperty("name").GetString() ?? "Unknown",
                                    CharacterName = castMember.TryGetProperty("character", out var c) ? c.GetString() : null,
                                    ProfileImageUrl = castMember.TryGetProperty("profile_path", out var pp) && pp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null,
                                    Roles = CastRole.Actor
                                };
                            }
                        }

                        if (ep.TryGetProperty("crew", out var crewArray) && crewArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var crewMember in crewArray.EnumerateArray())
                            {
                                var department = crewMember.TryGetProperty("department", out var dept) ? dept.GetString() : "";
                                var crewRole = MapDepartmentToRole(department);
                                if (crewRole == CastRole.None || crewRole == CastRole.Producer) continue;

                                var tmdbId = crewMember.GetProperty("id").GetInt32();

                                if (crewDict.TryGetValue(tmdbId, out var existing))
                                {
                                    existing.Roles |= crewRole;
                                }
                                else
                                {
                                    crewDict[tmdbId] = new CastMemberResult
                                    {
                                        TmdbId = tmdbId,
                                        Name = crewMember.GetProperty("name").GetString() ?? "Unknown",
                                        ProfileImageUrl = crewMember.TryGetProperty("profile_path", out var pp) && pp.GetString() != null ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null,
                                        Roles = crewRole
                                    };
                                }
                            }
                        }

                        result.Cast = crewDict.Values.ToList();
                        seasonEpisodes[eNum] = result;
                    }
                }
            }

            _tmdbSeasonCache[cacheKey] = seasonEpisodes;
        }

        if (seasonEpisodes.TryGetValue(episodeNumber, out var epResult))
        {
            return epResult;
        }

        return null;
    }

    public async Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        var lang = await GetLanguageAsync();
        var url = $"person/{personId}?api_key={apiKey}&language={lang}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var el = doc.RootElement;

        return new ActorMetadataResult
        {
            Biography = el.TryGetProperty("biography", out var bio) ? bio.GetString() : null,
            PlaceOfBirth = el.TryGetProperty("place_of_birth", out var pob) ? pob.GetString() : null,
            Birthday = DateTime.TryParse(el.TryGetProperty("birthday", out var bday) ? bday.GetString() : "", out var b)
                ? DateTime.SpecifyKind(b, DateTimeKind.Utc) : null,
            Deathday = DateTime.TryParse(el.TryGetProperty("deathday", out var dday) ? dday.GetString() : "", out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            ImdbId = el.TryGetProperty("imdb_id", out var imdb) ? imdb.GetString() : null,
            HomePage = el.TryGetProperty("homepage", out var hp) ? hp.GetString() : null
        };
    }

    private static CastRole MapDepartmentToRole(string? department) => department switch
    {
        "Directing" => CastRole.Director,
        "Writing" => CastRole.Writer,
        "Production" => CastRole.Producer,
        _ => CastRole.None
    };
}
