namespace Vora.Plugins.Dtos;

public enum MediaWebhookChangeKind
{
    Added,
    Removed,
    FolderRemoved,
    ScanLibrary
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

public sealed class MediaWebhookSettingDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();
    public string ShowWhen { get; set; } = string.Empty;
}

public static class MediaWebhookSettingKeys
{
    public const string Action = "action";
    public const string LibraryType = "library_type";
    public const string ScanLibrary = "scan_library";
    public const string EventPath = "event_path";
    public const string AddedEvents = "added_events";
    public const string RemovedEvents = "removed_events";
    public const string FolderRemovedEvents = "folder_removed_events";
    public const string TestEvents = "test_events";
    public const string FilePath = "file_path";
    public const string AlsoRemovedPath = "also_removed_path";
    public const string FolderPath = "folder_path";

    public const string IngestAction = "ingest";
    public const string ScanAction = "scan";
}
