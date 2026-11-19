using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Application.Settings;
using Vora.Domain.Entities.Webhooks;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Webhooks;

public interface IMediaWebhookManager
{
    Task<MediaWebhookReceipt> ReceiveAsync(string connectionId, string body, IReadOnlyList<string> presentedSecrets);

    List<WebhookProviderVM> GetProviders();

    Task<List<WebhookConnectionVM>> GetConnectionsAsync();

    Task<WebhookConnectionVM?> GetConnectionAsync(Guid id);

    Task<WebhookResult<WebhookConnectionVM>> CreateAsync(SaveWebhookConnectionRequest request);

    Task<WebhookResult<WebhookConnectionVM>> UpdateAsync(Guid id, SaveWebhookConnectionRequest request);

    Task<MediaWebhookSecretVM?> RegenerateSecretAsync(Guid id);

    Task<bool> DeleteAsync(Guid id);

    IReadOnlyList<MediaWebhookDelivery> GetDeliveries(int limit, Guid? connectionId);

    Task<WebhookPreviewVM> PreviewAsync(WebhookPreviewRequest request);

    Task<List<WebhookRequestServerVM>> GetRequestServersAsync();

    Task<WebhookMappingSuggestionVM?> SuggestMappingsAsync(Guid requestServerId);
}

public class MediaWebhookManager(
    IEnumerable<IMediaWebhookProvider> providers,
    IWebhookConnectionRepository connections,
    IWebhookSecretProtector secrets,
    IMediaWebhookQueue queue,
    IMediaWebhookLog log,
    ILibraryRepository libraryRepo,
    ISystemSettingsRepository settingsRepo,
    IMediaRepository mediaRepo,
    IRequestRepository requestRepo,
    IArrRootFolderClient rootFolders,
    ILogger<MediaWebhookManager> logger) : IMediaWebhookManager
{
    private const int MaxNameLength = 128;

    private static readonly IReadOnlyDictionary<string, string> NoSettings = new Dictionary<string, string>();

    public async Task<MediaWebhookReceipt> ReceiveAsync(string connectionId, string body, IReadOnlyList<string> presentedSecrets)
    {
        var notFound = new MediaWebhookReceipt(MediaWebhookStatus.NotFound, "Not found.");

        if (!Guid.TryParse(connectionId, out var id)) return notFound;

        var connection = await connections.GetAsync(id);
        if (connection == null || !connection.IsEnabled) return notFound;

        var provider = ProviderFor(connection.ProviderId);
        if (provider == null) return notFound;

        string secret;
        try
        {
            secret = secrets.Unprotect(connection.SecretCiphertext);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("The secret of webhook connection {Connection} cannot be read with this server's keys. Regenerate it.", connection.Name);
            return notFound;
        }

        if (string.IsNullOrWhiteSpace(secret)) return notFound;

        if (!presentedSecrets.Any(p => SecretsMatch(secret, p)))
        {
            logger.LogWarning("A request to webhook connection {Connection} carried the wrong secret.", connection.Name);
            return new MediaWebhookReceipt(MediaWebhookStatus.Unauthorized, "The secret is missing or wrong.");
        }

        var settings = ReadSettings(connection.SettingsJson);
        var libraryType = MediaWebhookResolvers.ToLibraryType(provider.ResolveLibraryKind(settings));

        MediaWebhookParseResult parsed;
        try
        {
            parsed = provider.Parse(body, settings);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new MediaWebhookReceipt(MediaWebhookStatus.BadRequest, "The body is not a payload this connection understands.");
        }

        if (parsed.IsTest)
        {
            log.Record(connection.Id, connection.Name, connection.ProviderId, parsed.InstanceName, "Test", string.Empty, "Test received. Vora is listening.", true);
            return new MediaWebhookReceipt(MediaWebhookStatus.Accepted, "Test received. Vora is listening.");
        }

        if (parsed.Changes.Count == 0)
        {
            log.Record(connection.Id, connection.Name, connection.ProviderId, parsed.InstanceName, parsed.EventName, string.Empty, "Nothing to do for this event.", true);
            return new MediaWebhookReceipt(MediaWebhookStatus.Accepted, "Nothing to do for this event.");
        }

        queue.Enqueue(new MediaWebhookWork(
            connection.Id,
            connection.Name,
            connection.ProviderId,
            parsed.InstanceName,
            parsed.EventName,
            libraryType,
            connection.PathMappings,
            connection.HandleDeletes,
            settings,
            parsed.Changes));

        return new MediaWebhookReceipt(MediaWebhookStatus.Accepted, $"Received {parsed.Changes.Count} change(s).");
    }

    public List<WebhookProviderVM> GetProviders() => providers
        .OrderBy(p => p.Id == "custom_webhook" ? 1 : 0)
        .ThenBy(p => p.Name)
        .Select(p => new WebhookProviderVM
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            DefaultConnectionName = p.DefaultConnectionName,
            SetupInstructions = p.SetupInstructions,
            LibraryKind = p.ResolveLibraryKind(NoSettings)?.ToString(),
            RequestServerProviderId = p.RequestServerProviderId,
            Settings = p.GetConnectionSettingDefinitions().ToList()
        })
        .ToList();

    public async Task<List<WebhookConnectionVM>> GetConnectionsAsync() =>
        (await connections.GetAllAsync())
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => ToVM(c, includeSecret: false))
            .ToList();

    public async Task<WebhookConnectionVM?> GetConnectionAsync(Guid id)
    {
        var connection = await connections.GetAsync(id);
        return connection == null ? null : ToVM(connection, includeSecret: true);
    }

    public async Task<WebhookResult<WebhookConnectionVM>> CreateAsync(SaveWebhookConnectionRequest request)
    {
        var provider = ProviderFor(request.ProviderId);
        if (provider == null) return WebhookResult<WebhookConnectionVM>.Invalid("Choose a template for this connection.");

        var checkedRequest = Validate(provider, request, out var settings);
        if (checkedRequest != null) return WebhookResult<WebhookConnectionVM>.Invalid(checkedRequest);

        var secret = NewSecret();
        var connection = new WebhookConnection
        {
            Name = request.Name.Trim(),
            ProviderId = provider.Id,
            IsEnabled = request.IsEnabled,
            HandleDeletes = request.HandleDeletes,
            PathMappings = (request.PathMappings ?? string.Empty).Trim(),
            SettingsJson = JsonSerializer.Serialize(settings),
            SecretCiphertext = secrets.Protect(secret)
        };

        await connections.AddAsync(connection);
        var vm = ToVM(connection, includeSecret: false);
        vm.Secret = secret;
        return WebhookResult<WebhookConnectionVM>.Ok(vm);
    }

    public async Task<WebhookResult<WebhookConnectionVM>> UpdateAsync(Guid id, SaveWebhookConnectionRequest request)
    {
        var connection = await connections.GetAsync(id);
        if (connection == null) return WebhookResult<WebhookConnectionVM>.Missing();

        var provider = ProviderFor(connection.ProviderId);
        if (provider == null) return WebhookResult<WebhookConnectionVM>.Invalid("The template this connection was made from is no longer installed.");

        var checkedRequest = Validate(provider, request, out var settings);
        if (checkedRequest != null) return WebhookResult<WebhookConnectionVM>.Invalid(checkedRequest);

        connection.Name = request.Name.Trim();
        connection.IsEnabled = request.IsEnabled;
        connection.HandleDeletes = request.HandleDeletes;
        connection.PathMappings = (request.PathMappings ?? string.Empty).Trim();
        connection.SettingsJson = JsonSerializer.Serialize(settings);

        await connections.UpdateAsync(connection);
        return WebhookResult<WebhookConnectionVM>.Ok(ToVM(connection, includeSecret: false));
    }

    public async Task<MediaWebhookSecretVM?> RegenerateSecretAsync(Guid id)
    {
        var connection = await connections.GetAsync(id);
        if (connection == null) return null;

        var secret = NewSecret();
        connection.SecretCiphertext = secrets.Protect(secret);
        await connections.UpdateAsync(connection);
        return new MediaWebhookSecretVM { Secret = secret };
    }

    public Task<bool> DeleteAsync(Guid id) => connections.DeleteAsync(id);

    public IReadOnlyList<MediaWebhookDelivery> GetDeliveries(int limit, Guid? connectionId) => log.Recent(limit, connectionId);

    public async Task<WebhookPreviewVM> PreviewAsync(WebhookPreviewRequest request)
    {
        var provider = ProviderFor(request.ProviderId);
        if (provider == null) return new WebhookPreviewVM { Error = "Choose a template first." };

        var settings = WithDefaults(provider, request.Settings);

        MediaWebhookParseResult parsed;
        try
        {
            parsed = provider.Parse(request.Body ?? string.Empty, settings);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new WebhookPreviewVM { Error = "That is not valid JSON." };
        }

        var preview = new WebhookPreviewVM { Ok = true, Event = parsed.EventName, IsTest = parsed.IsTest, Instance = parsed.InstanceName };
        if (parsed.Changes.Count == 0) return preview;

        var libraryType = MediaWebhookResolvers.ToLibraryType(provider.ResolveLibraryKind(settings));
        var resolver = libraryType is { } type && parsed.Changes.Any(c => c.Kind != MediaWebhookChangeKind.ScanLibrary)
            ? await MediaWebhookResolvers.CreateAsync(libraryRepo, settingsRepo, mediaRepo, type, request.PathMappings)
            : null;

        foreach (var change in parsed.Changes)
        {
            preview.Changes.Add(await PreviewChangeAsync(change, settings, resolver));
        }

        return preview;
    }

    public async Task<List<WebhookRequestServerVM>> GetRequestServersAsync()
    {
        var byRequestProvider = providers
            .Where(p => p.RequestServerProviderId != null)
            .ToDictionary(p => p.RequestServerProviderId!, p => p.Id, StringComparer.OrdinalIgnoreCase);

        return (await requestRepo.GetAllServersAsync())
            .Where(s => s.IsEnabled && byRequestProvider.ContainsKey(s.ProviderId))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new WebhookRequestServerVM { Id = s.Id, Name = s.Name, ProviderId = s.ProviderId, WebhookProviderId = byRequestProvider[s.ProviderId] })
            .ToList();
    }

    public async Task<WebhookMappingSuggestionVM?> SuggestMappingsAsync(Guid requestServerId)
    {
        var server = (await requestRepo.GetAllServersAsync()).FirstOrDefault(s => s.Id == requestServerId);
        var provider = server == null ? null : providers.FirstOrDefault(p => string.Equals(p.RequestServerProviderId, server.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (server == null || provider == null) return null;

        var suggestion = new WebhookMappingSuggestionVM { Name = server.Name };

        IReadOnlyList<string> roots;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            roots = await rootFolders.GetRootFoldersAsync(RequestServerAddress.BaseUrl(server.UseSsl, server.Hostname, server.Port, server.UrlBase), server.ApiKey, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            suggestion.Error = $"Could not read the root folders from {server.Name}: {ex.Message}";
            return suggestion;
        }

        var type = MediaWebhookResolvers.ToLibraryType(provider.ResolveLibraryKind(NoSettings));
        var folders = (await libraryRepo.GetAllProjectedAsync(l => new MediaWebhookLibrary(l.Id, l.Name, l.Type, l.FolderPaths, l.ExcludeFilters)))
            .Where(l => l.Type == type)
            .SelectMany(l => l.FolderPaths)
            .Distinct()
            .ToList();

        var suggested = MediaWebhookMappingSuggester.Suggest(roots, folders);
        suggestion.RootFolders = roots.ToList();
        suggestion.Mappings = suggested.Mappings;
        suggestion.UnmatchedRoots = suggested.Unmatched.ToList();
        return suggestion;
    }

    private async Task<WebhookPreviewChangeVM> PreviewChangeAsync(MediaWebhookChange change, Dictionary<string, string> settings, MediaWebhookPathResolver? resolver)
    {
        var item = new WebhookPreviewChangeVM { Kind = change.Kind.ToString(), ReportedPath = change.Path };

        if (change.Kind == MediaWebhookChangeKind.ScanLibrary)
        {
            var name = settings.TryGetValue(MediaWebhookSettingKeys.ScanLibrary, out var value) && Guid.TryParse(value, out var libraryId)
                ? await libraryRepo.GetProjectedByIdAsync(libraryId, l => l.Name)
                : null;
            item.LibraryName = name;
            item.Note = name == null ? "Choose a library to scan." : $"Would scan {name}.";
            return item;
        }

        if (resolver == null)
        {
            item.Note = "Choose which kind of library the files belong to.";
            return item;
        }

        var resolution = await resolver.ResolveAsync(change.Path);
        if (resolution.Target is not { } target)
        {
            item.Note = resolution.Reason;
            return item;
        }

        item.MappedPath = target.Path;
        item.LibraryName = target.Library.Name;
        item.ExistsOnDisk = change.Kind == MediaWebhookChangeKind.FolderRemoved ? Directory.Exists(target.Path) : File.Exists(target.Path);
        item.Note = (change.Kind, item.ExistsOnDisk) switch
        {
            (MediaWebhookChangeKind.Added, true) => "Would be ingested.",
            (MediaWebhookChangeKind.Added, _) => "Not on disk yet. Vora would wait a few minutes for it.",
            (MediaWebhookChangeKind.Removed, true) => "Still on disk, so it would be kept.",
            (MediaWebhookChangeKind.Removed, _) => "Gone, so it would move to Media Trash.",
            (_, true) => "The folder is still there: only files that are gone from it would move to Media Trash.",
            _ => "Its files would move to Media Trash."
        };
        return item;
    }

    private IMediaWebhookProvider? ProviderFor(string id) => providers.FirstOrDefault(p => p.Id == id);

    private WebhookConnectionVM ToVM(WebhookConnection connection, bool includeSecret)
    {
        var latest = log.Latest(connection.Id);
        return new WebhookConnectionVM
        {
            Id = connection.Id,
            Name = connection.Name,
            ProviderId = connection.ProviderId,
            ProviderName = ProviderFor(connection.ProviderId)?.Name ?? connection.ProviderId,
            IsEnabled = connection.IsEnabled,
            HandleDeletes = connection.HandleDeletes,
            ReceivePath = $"/api/webhooks/{connection.Id}",
            PathMappings = connection.PathMappings,
            Settings = ReadSettings(connection.SettingsJson),
            CreatedAt = connection.CreatedAt,
            LastDeliveryAt = latest?.At,
            LastDeliveryOk = latest?.Ok,
            Secret = includeSecret ? TryUnprotect(connection) : null
        };
    }

    private string? TryUnprotect(WebhookConnection connection)
    {
        try
        {
            return secrets.Unprotect(connection.SecretCiphertext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string? Validate(IMediaWebhookProvider provider, SaveWebhookConnectionRequest request, out Dictionary<string, string> settings)
    {
        settings = WithDefaults(provider, request.Settings);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Give the connection a name.";
        if (name.Length > MaxNameLength) return $"The name can be at most {MaxNameLength} characters.";

        var bad = MediaWebhookPathMappings.FirstBadLine(request.PathMappings);
        if (bad != null) return $"The path mapping \"{bad}\" needs a => between the two folders.";

        foreach (var definition in provider.GetConnectionSettingDefinitions())
        {
            if (!IsVisible(definition, settings)) continue;

            var value = settings[definition.Key];
            if (definition.Required && string.IsNullOrWhiteSpace(value)) return $"{definition.Label} is required.";
            if (definition.Type == "select" && value.Length > 0 && !definition.Options.Any(o => OptionValue(o) == value)) return $"{definition.Label} is not one of the choices.";
            if (definition.Type == "library" && value.Length > 0 && !Guid.TryParse(value, out _)) return $"{definition.Label} is not a library.";
        }

        return null;
    }

    private static Dictionary<string, string> WithDefaults(IMediaWebhookProvider provider, Dictionary<string, string>? given)
    {
        var result = new Dictionary<string, string>();
        foreach (var definition in provider.GetConnectionSettingDefinitions())
        {
            result[definition.Key] = given != null && given.TryGetValue(definition.Key, out var value)
                ? value?.Trim() ?? string.Empty
                : definition.DefaultValue;
        }
        return result;
    }

    private static bool IsVisible(MediaWebhookSettingDefinition definition, IReadOnlyDictionary<string, string> settings)
    {
        if (string.IsNullOrEmpty(definition.ShowWhen)) return true;

        var at = definition.ShowWhen.IndexOf('=');
        if (at <= 0) return true;

        return settings.TryGetValue(definition.ShowWhen[..at], out var current) && current == definition.ShowWhen[(at + 1)..];
    }

    private static string OptionValue(string option)
    {
        var at = option.IndexOf('=');
        return at < 0 ? option : option[..at];
    }

    private static Dictionary<string, string> ReadSettings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool SecretsMatch(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
}
