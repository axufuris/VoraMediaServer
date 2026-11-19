using Vora.Domain.Enums;
using Vora.Plugins.Dtos;

namespace Vora.Application.Webhooks;

public enum MediaWebhookStatus
{
    Accepted,
    NotFound,
    Unauthorized,
    BadRequest
}

public sealed record MediaWebhookReceipt(MediaWebhookStatus Status, string Message);

public sealed record MediaWebhookWork(
    Guid ConnectionId,
    string ConnectionName,
    string ProviderId,
    string? InstanceName,
    string EventName,
    LibraryType? LibraryType,
    string PathMappings,
    bool HandleDeletes,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<MediaWebhookChange> Changes);

public sealed record MediaWebhookLibrary(Guid Id, string Name, LibraryType Type, List<string> FolderPaths, List<string> ExcludeFilters);

public sealed record MediaWebhookTarget(MediaWebhookLibrary Library, string Root, string Path);

public sealed record MediaWebhookResolution(MediaWebhookTarget? Target, string Reason)
{
    public static MediaWebhookResolution Found(MediaWebhookTarget target) => new(target, string.Empty);

    public static MediaWebhookResolution NotFound(string reason) => new(null, reason);
}

public sealed record MediaWebhookDelivery(
    Guid Id,
    DateTime At,
    Guid ConnectionId,
    string ConnectionName,
    string ProviderId,
    string? Instance,
    string Event,
    string Path,
    string Outcome,
    bool Ok);

public sealed record MediaWebhookTimings(IReadOnlyList<TimeSpan> AddRetryDelays, TimeSpan RemoveSettle, TimeSpan DuplicateWindow)
{
    public static MediaWebhookTimings Default { get; } = new(
        new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120) },
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(1));

    public static MediaWebhookTimings Immediate { get; } = new(new[] { TimeSpan.Zero, TimeSpan.Zero }, TimeSpan.Zero, TimeSpan.FromMinutes(1));
}

public sealed class WebhookResult<T>
{
    public T? Value { get; private init; }
    public string? Error { get; private init; }
    public bool IsMissing { get; private init; }

    public bool Succeeded => Error == null && !IsMissing;

    public static WebhookResult<T> Ok(T value) => new() { Value = value };

    public static WebhookResult<T> Invalid(string error) => new() { Error = error };

    public static WebhookResult<T> Missing() => new() { IsMissing = true };
}

public sealed class WebhookProviderVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultConnectionName { get; set; } = string.Empty;
    public string SetupInstructions { get; set; } = string.Empty;
    public string? LibraryKind { get; set; }
    public string? RequestServerProviderId { get; set; }
    public List<MediaWebhookSettingDefinition> Settings { get; set; } = new();
}

public sealed class WebhookConnectionVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool HandleDeletes { get; set; }
    public string ReceivePath { get; set; } = string.Empty;
    public string PathMappings { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public bool? LastDeliveryOk { get; set; }
    public string? Secret { get; set; }
}

public sealed class SaveWebhookConnectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool HandleDeletes { get; set; } = true;
    public string PathMappings { get; set; } = string.Empty;
    public Dictionary<string, string>? Settings { get; set; }
}

public sealed class MediaWebhookSecretVM
{
    public string Secret { get; set; } = string.Empty;
}

public sealed class WebhookPreviewRequest
{
    public string ProviderId { get; set; } = string.Empty;
    public Dictionary<string, string>? Settings { get; set; }
    public string PathMappings { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class WebhookPreviewChangeVM
{
    public string Kind { get; set; } = string.Empty;
    public string ReportedPath { get; set; } = string.Empty;
    public string? MappedPath { get; set; }
    public string? LibraryName { get; set; }
    public bool? ExistsOnDisk { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class WebhookPreviewVM
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string Event { get; set; } = string.Empty;
    public bool IsTest { get; set; }
    public string? Instance { get; set; }
    public List<WebhookPreviewChangeVM> Changes { get; set; } = new();
}

public sealed class WebhookRequestServerVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string WebhookProviderId { get; set; } = string.Empty;
}

public sealed class WebhookMappingSuggestionVM
{
    public string Name { get; set; } = string.Empty;
    public List<string> RootFolders { get; set; } = new();
    public string Mappings { get; set; } = string.Empty;
    public List<string> UnmatchedRoots { get; set; } = new();
    public string? Error { get; set; }
}
