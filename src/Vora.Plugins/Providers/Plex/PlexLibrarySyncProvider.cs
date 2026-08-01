using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Plex;

public class PlexLibrarySyncProvider : ILibrarySyncProvider
{
    public const string HttpClientName = "PlexLibrarySync";

    private static readonly SemaphoreSlim ClientIdentifierLock = new(1, 1);

    private const string PlexApiBaseUrl = "https://plex.tv/api/v2/";
    private const string PlexVerificationUrl = "https://plex.tv/link";
    private const string ClientIdentifierSettingKey = "client_identifier";
    private const string ProductName = "Vora";
    private const string DeviceName = "Vora Media Server";
    private const string ProductVersion = "1.0.0";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginSettingsProvider _settings;
    private readonly ILogger<PlexLibrarySyncProvider> _logger;

    public string Id => "plex_library_sync";
    public string Name => "Plex Library Sync";
    public string ProviderName => "Plex";
    public string Version => "1.0.0";
    public string Description => "Imports watch state and ratings from a Plex Media Server during a one-time migration.";
    public bool IsSystemPlugin => true;
    public string Type => "Library_Sync";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow, LibraryKind.Music };

    public PlexLibrarySyncProvider(
        IHttpClientFactory httpClientFactory,
        IPluginSettingsProvider settings,
        ILogger<PlexLibrarySyncProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    public async Task<LibrarySyncPinDto> CreatePinAsync(CancellationToken cancellationToken = default)
    {
        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{PlexApiBaseUrl}pins");
        ApplyPlexHeaders(request, clientIdentifier);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Plex pin creation failed ({StatusCode}): {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"Plex pin creation failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var pinId = ReadPinIdentifier(root);
        var code = root.GetProperty("code").GetString() ?? throw new InvalidOperationException("Plex pin response missing 'code'.");

        DateTime? expiresAt = null;
        if (root.TryGetProperty("expiresAt", out var expiresProp) && expiresProp.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(expiresProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                expiresAt = parsed;
            }
        }

        return new LibrarySyncPinDto
        {
            PinId = pinId,
            Code = code,
            VerificationUrl = PlexVerificationUrl,
            ExpiresAt = expiresAt
        };
    }

    public async Task<LibrarySyncPinStatusDto> PollPinAsync(string pinId, CancellationToken cancellationToken = default)
    {
        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexApiBaseUrl}pins/{pinId}");
        ApplyPlexHeaders(request, clientIdentifier);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new LibrarySyncPinStatusDto { PinId = pinId, Status = LibrarySyncPinStatus.Expired };
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Plex pin poll failed ({StatusCode}): {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"Plex pin poll failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("expiresAt", out var expiresProp) && expiresProp.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(expiresProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                if (parsed < DateTime.UtcNow)
                {
                    return new LibrarySyncPinStatusDto { PinId = pinId, Status = LibrarySyncPinStatus.Expired };
                }
            }
        }

        string? authToken = null;
        if (root.TryGetProperty("authToken", out var tokenProp) && tokenProp.ValueKind == JsonValueKind.String)
        {
            authToken = tokenProp.GetString();
        }

        if (string.IsNullOrEmpty(authToken))
        {
            return new LibrarySyncPinStatusDto { PinId = pinId, Status = LibrarySyncPinStatus.Pending };
        }

        var username = await FetchPlexUsernameAsync(authToken, clientIdentifier, cancellationToken);
        return new LibrarySyncPinStatusDto
        {
            PinId = pinId,
            Status = LibrarySyncPinStatus.Authorized,
            Token = new LibrarySyncTokenDto { AccessToken = authToken, Username = username }
        };
    }

    public async Task<IReadOnlyList<RemoteServerDto>> ListServersAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(accessToken));
        }

        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexApiBaseUrl}resources?includeHttps=1&includeRelay=1");
        ApplyPlexHeaders(request, clientIdentifier);
        request.Headers.Add("X-Plex-Token", accessToken);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Plex resource listing failed ({StatusCode}): {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"Plex resource listing failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RemoteServerDto>();
        }

        var results = new List<RemoteServerDto>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var server = TryProjectServer(element);
            if (server is not null) results.Add(server);
        }
        return results;
    }

    public async Task<IReadOnlyList<RemoteAccountDto>> ListAccountsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(accessToken));
        }

        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var accounts = new List<RemoteAccountDto>();
        var homeMembers = await TryFetchHomeUsersAsync(client, accessToken, clientIdentifier, cancellationToken);
        if (homeMembers is not null) accounts.AddRange(homeMembers);

        var owner = await TryFetchOwnerAccountAsync(client, accessToken, clientIdentifier, cancellationToken);
        if (owner is not null)
        {
            var idx = accounts.FindIndex(a => string.Equals(a.Id, owner.Id, StringComparison.Ordinal));
            if (idx >= 0) accounts[idx] = owner;
            else accounts.Insert(0, owner);
        }

        return accounts;
    }

    public async Task<string> ResolveUserTokenAsync(string adminAccessToken, string accountId, string? pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adminAccessToken))
        {
            throw new ArgumentException("Admin access token is required.", nameof(adminAccessToken));
        }
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("Account id is required.", nameof(accountId));
        }

        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var owner = await TryFetchOwnerAccountAsync(client, adminAccessToken, clientIdentifier, cancellationToken);
        if (owner is not null && string.Equals(owner.Id, accountId, StringComparison.Ordinal))
        {
            return adminAccessToken;
        }

        var uri = $"{PlexApiBaseUrl}home/users/{accountId}/switch";
        if (!string.IsNullOrEmpty(pin))
        {
            uri += $"?pin={Uri.EscapeDataString(pin)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        ApplyPlexHeaders(request, clientIdentifier);
        request.Headers.Add("X-Plex-Token", adminAccessToken);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Plex home switch for account {AccountId} returned {StatusCode}: {Body}", accountId, (int)response.StatusCode, body);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("The PIN for this user is invalid or missing.");
            }

            _logger.LogInformation("Falling back to admin token for account {AccountId} (switch unavailable).", accountId);
            return adminAccessToken;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var token = ReadStringProperty(root, "authToken");
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Plex home switch for account {AccountId} returned no authToken; falling back to admin token.", accountId);
            return adminAccessToken;
        }

        return token;
    }

    public async Task<IReadOnlyList<RemoteLibraryDto>> ListLibrariesAsync(string connectionUri, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionUri))
        {
            throw new ArgumentException("Connection URI is required.", nameof(connectionUri));
        }
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(accessToken));
        }

        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var sections = await FetchLibrarySectionsAsync(client, connectionUri.TrimEnd('/'), accessToken, clientIdentifier, cancellationToken);

        return sections
            .Select(s => new RemoteLibraryDto
            {
                Key = s.Key,
                Name = s.Title,
                Kind = MapKind(s.Type)
            })
            .ToList();
    }

    public async Task<RemoteUserDataDto> FetchUserDataAsync(string connectionUri, string userAccessToken, RemoteSyncScopeDto scope, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionUri))
        {
            throw new ArgumentException("Connection URI is required.", nameof(connectionUri));
        }
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            throw new ArgumentException("User access token is required.", nameof(userAccessToken));
        }

        var clientIdentifier = await GetOrCreateClientIdentifierAsync();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var baseUri = connectionUri.TrimEnd('/');

        var allSections = await FetchLibrarySectionsAsync(client, baseUri, userAccessToken, clientIdentifier, cancellationToken);
        var selectedKeys = new HashSet<string>(scope.LibrarySectionKeys, StringComparer.Ordinal);
        var sections = allSections.Where(s => selectedKeys.Contains(s.Key)).ToList();

        var watchStates = new List<RemoteWatchStateDto>();
        var ratings = new List<RemoteRatingDto>();

        foreach (var section in sections)
        {
            if (section.Type == "movie")
            {
                await WalkSectionAsync(client, baseUri, userAccessToken, clientIdentifier, section.Key, type: null, RemoteMediaKind.Movie, scope, watchStates, ratings, cancellationToken);
            }
            else if (section.Type == "show")
            {
                await WalkSectionAsync(client, baseUri, userAccessToken, clientIdentifier, section.Key, type: 4, RemoteMediaKind.Episode, scope, watchStates, ratings, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Plex fetch: {SelectedSections}/{AllSections} sections matched; collected {WatchCount} watch states and {RatingCount} ratings.",
            sections.Count, allSections.Count, watchStates.Count, ratings.Count);

        return new RemoteUserDataDto
        {
            WatchStates = watchStates,
            Ratings = ratings
        };
    }

    private static RemoteLibraryKind MapKind(string plexType) => plexType switch
    {
        "movie" => RemoteLibraryKind.Movie,
        "show" => RemoteLibraryKind.Show,
        "artist" => RemoteLibraryKind.Music,
        _ => RemoteLibraryKind.Other
    };

    private async Task<IReadOnlyList<PlexLibrarySection>> FetchLibrarySectionsAsync(HttpClient client, string baseUri, string userToken, string clientIdentifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUri}/library/sections");
        ApplyPlexHeaders(request, clientIdentifier);
        request.Headers.Add("X-Plex-Token", userToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Plex /library/sections returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"Failed to list Plex library sections (status {(int)response.StatusCode}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var results = new List<PlexLibrarySection>();
        if (root.TryGetProperty("MediaContainer", out var container)
            && container.TryGetProperty("Directory", out var directory)
            && directory.ValueKind == JsonValueKind.Array)
        {
            foreach (var dir in directory.EnumerateArray())
            {
                var key = ReadStringProperty(dir, "key");
                var type = ReadStringProperty(dir, "type");
                var title = ReadStringProperty(dir, "title") ?? "Unnamed library";
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(type)) continue;
                results.Add(new PlexLibrarySection { Key = key, Type = type, Title = title });
            }
        }
        return results;
    }

    private async Task WalkSectionAsync(
        HttpClient client,
        string baseUri,
        string userToken,
        string clientIdentifier,
        string sectionKey,
        int? type,
        RemoteMediaKind kind,
        RemoteSyncScopeDto scope,
        List<RemoteWatchStateDto> watchStates,
        List<RemoteRatingDto> ratings,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var start = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri = $"{baseUri}/library/sections/{sectionKey}/all?includeGuids=1";
            if (type.HasValue) uri += $"&type={type.Value}";

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyPlexHeaders(request, clientIdentifier);
            request.Headers.Add("X-Plex-Token", userToken);
            request.Headers.Add("X-Plex-Container-Start", start.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Plex-Container-Size", pageSize.ToString(CultureInfo.InvariantCulture));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Plex section {SectionKey} returned {StatusCode}: {Body}", sectionKey, (int)response.StatusCode, body);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (!root.TryGetProperty("MediaContainer", out var container))
            {
                return;
            }

            var totalSize = container.TryGetProperty("totalSize", out var totalProp) && totalProp.ValueKind == JsonValueKind.Number
                ? totalProp.GetInt32()
                : 0;

            if (start == 0)
            {
                _logger.LogInformation("Plex section {SectionKey} ({Kind}): {Total} items to scan.", sectionKey, kind, totalSize);
            }

            if (!container.TryGetProperty("Metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var pageCount = 0;
            foreach (var item in metadata.EnumerateArray())
            {
                pageCount++;
                ProjectMetadataItem(item, kind, scope, watchStates, ratings);
            }

            start += pageCount;
            if (pageCount == 0 || (totalSize > 0 && start >= totalSize) || pageCount < pageSize)
            {
                return;
            }
        }
    }

    private static void ProjectMetadataItem(JsonElement item, RemoteMediaKind kind, RemoteSyncScopeDto scope, List<RemoteWatchStateDto> watchStates, List<RemoteRatingDto> ratings)
    {
        var externalIds = ReadGuids(item);
        if (!externalIds.HasAny) return;

        if (scope.IncludeWatchState)
        {
            var viewCount = item.TryGetProperty("viewCount", out var vcProp) && vcProp.ValueKind == JsonValueKind.Number
                ? vcProp.GetInt32()
                : 0;
            var viewOffsetMs = item.TryGetProperty("viewOffset", out var voProp) && voProp.ValueKind == JsonValueKind.Number
                ? voProp.GetInt64()
                : 0;
            var lastViewedUnix = item.TryGetProperty("lastViewedAt", out var lvProp) && lvProp.ValueKind == JsonValueKind.Number
                ? lvProp.GetInt64()
                : 0;

            if (viewCount > 0 || viewOffsetMs > 0 || lastViewedUnix > 0)
            {
                watchStates.Add(new RemoteWatchStateDto
                {
                    ExternalIds = externalIds,
                    Kind = kind,
                    IsPlayed = viewCount > 0,
                    ResumePositionSeconds = viewOffsetMs / 1000.0,
                    LastPlayedAt = lastViewedUnix > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(lastViewedUnix).UtcDateTime
                        : null
                });
            }
        }

        if (scope.IncludeRatings && item.TryGetProperty("userRating", out var urProp) && urProp.ValueKind == JsonValueKind.Number)
        {
            var rating = (decimal)urProp.GetDouble();
            if (rating > 0m)
            {
                var lastRatedUnix = item.TryGetProperty("lastRatedAt", out var lrProp) && lrProp.ValueKind == JsonValueKind.Number
                    ? lrProp.GetInt64()
                    : 0;
                ratings.Add(new RemoteRatingDto
                {
                    ExternalIds = externalIds,
                    Kind = kind,
                    Rating = rating,
                    RatedAt = lastRatedUnix > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(lastRatedUnix).UtcDateTime
                        : null
                });
            }
        }
    }

    private static RemoteExternalIdsDto ReadGuids(JsonElement item)
    {
        var dto = new RemoteExternalIdsDto();
        if (!item.TryGetProperty("Guid", out var guids) || guids.ValueKind != JsonValueKind.Array)
        {
            return dto;
        }

        foreach (var entry in guids.EnumerateArray())
        {
            var raw = ReadStringProperty(entry, "id");
            if (string.IsNullOrEmpty(raw)) continue;

            if (raw.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
            {
                dto.ImdbId = raw[("imdb://").Length..];
            }
            else if (raw.StartsWith("tmdb://", StringComparison.OrdinalIgnoreCase))
            {
                dto.TmdbId = raw[("tmdb://").Length..];
            }
            else if (raw.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase))
            {
                dto.TvdbId = raw[("tvdb://").Length..];
            }
        }
        return dto;
    }

    private async Task<IReadOnlyList<RemoteAccountDto>?> TryFetchHomeUsersAsync(HttpClient client, string accessToken, string clientIdentifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexApiBaseUrl}home/users");
        ApplyPlexHeaders(request, clientIdentifier);
        request.Headers.Add("X-Plex-Token", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Plex home users endpoint returned {StatusCode}; falling back to owner-only.", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        if (!root.TryGetProperty("users", out var usersProp) || usersProp.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var results = new List<RemoteAccountDto>();
        foreach (var entry in usersProp.EnumerateArray())
        {
            var account = TryProjectHomeMember(entry);
            if (account is not null) results.Add(account);
        }
        return results;
    }

    private async Task<RemoteAccountDto?> TryFetchOwnerAccountAsync(HttpClient client, string accessToken, string clientIdentifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexApiBaseUrl}user");
        ApplyPlexHeaders(request, clientIdentifier);
        request.Headers.Add("X-Plex-Token", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Plex /user endpoint returned {StatusCode}; cannot resolve owner.", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var id = ReadStringProperty(root, "uuid");
        if (string.IsNullOrEmpty(id)) return null;

        var displayName = ReadStringProperty(root, "title")
            ?? ReadStringProperty(root, "friendlyName")
            ?? ReadStringProperty(root, "username")
            ?? "Owner";

        return new RemoteAccountDto
        {
            Id = id,
            DisplayName = displayName,
            Kind = RemoteAccountKind.Owner,
            HasPin = false,
            AvatarUrl = ReadStringProperty(root, "thumb"),
            Email = ReadStringProperty(root, "email")
        };
    }

    private static RemoteAccountDto? TryProjectHomeMember(JsonElement element)
    {
        var id = ReadStringProperty(element, "uuid");
        if (string.IsNullOrEmpty(id)) return null;

        var displayName = ReadStringProperty(element, "title")
            ?? ReadStringProperty(element, "friendlyName")
            ?? ReadStringProperty(element, "username")
            ?? "Home User";

        var isAdmin = element.TryGetProperty("admin", out var adminProp) && adminProp.ValueKind == JsonValueKind.True;
        var hasPin = element.TryGetProperty("protected", out var protectedProp) && protectedProp.ValueKind == JsonValueKind.True;

        return new RemoteAccountDto
        {
            Id = id,
            DisplayName = displayName,
            Kind = isAdmin ? RemoteAccountKind.Owner : RemoteAccountKind.Home,
            HasPin = hasPin,
            AvatarUrl = ReadStringProperty(element, "thumb"),
            Email = ReadStringProperty(element, "email")
        };
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var value = prop.GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static RemoteServerDto? TryProjectServer(JsonElement element)
    {
        if (!element.TryGetProperty("provides", out var providesProp) || providesProp.ValueKind != JsonValueKind.String) return null;

        var provides = providesProp.GetString();
        if (string.IsNullOrEmpty(provides) || !provides.Split(',').Any(p => p.Trim().Equals("server", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var clientIdentifier = ReadStringProperty(element, "clientIdentifier");
        if (string.IsNullOrEmpty(clientIdentifier)) return null;

        var name = ReadStringProperty(element, "name") ?? "Unnamed Server";
        var owned = element.TryGetProperty("owned", out var ownedProp) && ownedProp.ValueKind == JsonValueKind.True;

        string? ownerName = null;
        if (!owned)
        {
            ownerName = ReadStringProperty(element, "sourceTitle");
        }

        var platform = ReadStringProperty(element, "platform");
        var productVersion = ReadStringProperty(element, "productVersion");
        var isOnline = element.TryGetProperty("presence", out var presenceProp) && presenceProp.ValueKind == JsonValueKind.True;

        var connections = new List<RemoteConnectionDto>();
        if (element.TryGetProperty("connections", out var connectionsProp) && connectionsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var connEl in connectionsProp.EnumerateArray())
            {
                var uri = ReadStringProperty(connEl, "uri");
                if (string.IsNullOrEmpty(uri)) continue;

                var isLocal = connEl.TryGetProperty("local", out var localProp) && localProp.ValueKind == JsonValueKind.True;
                var isRelay = connEl.TryGetProperty("relay", out var relayProp) && relayProp.ValueKind == JsonValueKind.True;
                var isHttps = connEl.TryGetProperty("protocol", out var protoProp)
                    && protoProp.ValueKind == JsonValueKind.String
                    && string.Equals(protoProp.GetString(), "https", StringComparison.OrdinalIgnoreCase);

                connections.Add(new RemoteConnectionDto
                {
                    Uri = uri,
                    IsLocal = isLocal,
                    IsHttps = isHttps,
                    IsRelay = isRelay
                });
            }
        }

        return new RemoteServerDto
        {
            ClientIdentifier = clientIdentifier,
            Name = name,
            IsOwned = owned,
            OwnerName = ownerName,
            Platform = platform,
            ProductVersion = productVersion,
            IsOnline = isOnline,
            Connections = connections
        };
    }

    private static string ReadPinIdentifier(JsonElement root)
    {
        var idProp = root.GetProperty("id");
        return idProp.ValueKind switch
        {
            JsonValueKind.Number => idProp.GetInt64().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => idProp.GetString() ?? throw new InvalidOperationException("Plex pin 'id' was an empty string."),
            _ => throw new InvalidOperationException($"Plex pin 'id' had unexpected JSON kind {idProp.ValueKind}.")
        };
    }

    private async Task<string?> FetchPlexUsernameAsync(string accessToken, string clientIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlexApiBaseUrl}user");
            ApplyPlexHeaders(request, clientIdentifier);
            request.Headers.Add("X-Plex-Token", accessToken);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            return ReadStringProperty(root, "username") ?? ReadStringProperty(root, "title");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Plex username for newly authorized token.");
            return null;
        }
    }

    private async Task<string> GetOrCreateClientIdentifierAsync()
    {
        var existing = await _settings.GetSettingAsync(Id, ClientIdentifierSettingKey);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        await ClientIdentifierLock.WaitAsync();
        try
        {
            existing = await _settings.GetSettingAsync(Id, ClientIdentifierSettingKey);
            if (!string.IsNullOrWhiteSpace(existing)) return existing;

            var newIdentifier = Guid.NewGuid().ToString("N");
            await _settings.SetSettingAsync(Id, ClientIdentifierSettingKey, newIdentifier);
            _logger.LogInformation("Generated new Plex client identifier for Vora install.");
            return newIdentifier;
        }
        finally
        {
            ClientIdentifierLock.Release();
        }
    }

    private static void ApplyPlexHeaders(HttpRequestMessage request, string clientIdentifier)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Plex-Client-Identifier", clientIdentifier);
        request.Headers.Add("X-Plex-Product", ProductName);
        request.Headers.Add("X-Plex-Device", DeviceName);
        request.Headers.Add("X-Plex-Version", ProductVersion);
    }

    private sealed class PlexLibrarySection
    {
        public required string Key { get; init; }
        public required string Type { get; init; }
        public required string Title { get; init; }
    }
}
