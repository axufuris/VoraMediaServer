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

public sealed record MediaWebhookWork(string ProviderId, string? InstanceName, string EventName, LibraryType LibraryType, IReadOnlyList<MediaWebhookChange> Changes);

public sealed record MediaWebhookLibrary(Guid Id, string Name, LibraryType Type, List<string> FolderPaths, List<string> ExcludeFilters);

public sealed record MediaWebhookTarget(MediaWebhookLibrary Library, string Root, string Path);

public sealed record MediaWebhookResolution(MediaWebhookTarget? Target, string Reason)
{
    public static MediaWebhookResolution Found(MediaWebhookTarget target) => new(target, string.Empty);

    public static MediaWebhookResolution NotFound(string reason) => new(null, reason);
}

public sealed record MediaWebhookDelivery(Guid Id, DateTime At, string ProviderId, string? Instance, string Event, string Path, string Outcome, bool Ok);

public sealed record MediaWebhookTimings(IReadOnlyList<TimeSpan> AddRetryDelays, TimeSpan RemoveSettle, TimeSpan DuplicateWindow)
{
    public static MediaWebhookTimings Default { get; } = new(
        new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120) },
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(1));

    public static MediaWebhookTimings Immediate { get; } = new(new[] { TimeSpan.Zero, TimeSpan.Zero }, TimeSpan.Zero, TimeSpan.FromMinutes(1));
}

public sealed class MediaWebhookProviderVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LibraryKind { get; set; } = string.Empty;
    public string ReceivePath { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class MediaWebhookSecretVM
{
    public string Secret { get; set; } = string.Empty;
}
