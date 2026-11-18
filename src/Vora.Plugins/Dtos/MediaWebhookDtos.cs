namespace Vora.Plugins.Dtos;

public enum MediaWebhookChangeKind
{
    Added,
    Removed,
    FolderRemoved
}

public sealed record MediaWebhookChange(MediaWebhookChangeKind Kind, string Path);

public sealed class MediaWebhookParseResult
{
    public bool IsTest { get; init; }
    public string EventName { get; init; } = string.Empty;
    public string? InstanceName { get; init; }
    public IReadOnlyList<MediaWebhookChange> Changes { get; init; } = Array.Empty<MediaWebhookChange>();

    public static MediaWebhookParseResult Test(string? instanceName) =>
        new() { IsTest = true, EventName = "Test", InstanceName = instanceName };

    public static MediaWebhookParseResult Ignored(string eventName, string? instanceName) =>
        new() { EventName = eventName, InstanceName = instanceName };

    public static MediaWebhookParseResult Of(string eventName, string? instanceName, IEnumerable<MediaWebhookChange> changes) =>
        new() { EventName = eventName, InstanceName = instanceName, Changes = changes.ToList() };
}

public static class MediaWebhookSettingKeys
{
    public const string Secret = "secret";
    public const string PathMappings = "path_mappings";
}
