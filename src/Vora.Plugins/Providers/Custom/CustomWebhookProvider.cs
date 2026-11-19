using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Custom;

public class CustomWebhookProvider : IMediaWebhookProvider
{
    public string Id => "custom_webhook";
    public string Name => "Custom webhook";
    public string Description => "Any system that can send a webhook: tell Vora which field holds the file path and which event names mean added or removed, or have it scan a library whenever something arrives.";
    public string Version => "1.0.0";
    public string Type => "MediaWebhook";
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;
    public string DefaultConnectionName => "Custom webhook";

    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[]
    {
        LibraryKind.Movie,
        LibraryKind.TvShow,
        LibraryKind.Music,
        LibraryKind.HomeVideo,
    };

    public string SetupInstructions =>
        "Have the system send a POST or PUT with a JSON body to the address above.\n" +
        "Send the secret as the password of Basic auth, as an X-Api-Key header, or as ?token= on the address.\n" +
        "Below, tell Vora where the event name and the file's full path are in the JSON, and which event names mean a file was added, removed or a folder removed. The defaults suit a body like {\"event\":\"added\",\"path\":\"/media/movies/Film/Film.mkv\"}.\n" +
        "Use Try a sample payload to see what Vora would do with a real body before you save.";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Array.Empty<PluginSettingDefinitionDto>();

    public IEnumerable<MediaWebhookSettingDefinition> GetConnectionSettingDefinitions() => new List<MediaWebhookSettingDefinition>
    {
        new()
        {
            Key = MediaWebhookSettingKeys.Action,
            Label = "What to do when an event arrives",
            Type = "select",
            DefaultValue = MediaWebhookSettingKeys.IngestAction,
            Required = true,
            Options = new List<string> { $"{MediaWebhookSettingKeys.IngestAction}=Ingest or remove the file named in the payload", $"{MediaWebhookSettingKeys.ScanAction}=Scan a library" }
        },
        new()
        {
            Key = MediaWebhookSettingKeys.LibraryType,
            Label = "Kind of library the files belong to",
            Type = "select",
            DefaultValue = "Movie",
            Required = true,
            ShowWhen = $"{MediaWebhookSettingKeys.Action}={MediaWebhookSettingKeys.IngestAction}",
            Options = new List<string> { "Movie=Movies", "TvShow=TV shows", "Music=Music", "HomeVideo=Home videos" }
        },
        new()
        {
            Key = MediaWebhookSettingKeys.ScanLibrary,
            Label = "Library to scan",
            Type = "library",
            Required = true,
            ShowWhen = $"{MediaWebhookSettingKeys.Action}={MediaWebhookSettingKeys.ScanAction}",
            Description = "Every matching event starts a scan of this library. Events that arrive while one is queued or running are folded into it."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.EventPath,
            Label = "Where the event name is",
            DefaultValue = "event",
            Placeholder = "eventType",
            Description = "The field of the JSON body that holds the event name, such as eventType or action. Leave it empty to treat every request as a file being added (or a scan)."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.AddedEvents,
            Label = "Event names that mean added or changed",
            DefaultValue = "added",
            Placeholder = "added, import, download",
            Description = "Separate several with commas. Case does not matter."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.RemovedEvents,
            Label = "Event names that mean a file was removed",
            DefaultValue = "removed",
            Placeholder = "removed, deleted"
        },
        new()
        {
            Key = MediaWebhookSettingKeys.FolderRemovedEvents,
            Label = "Event names that mean a folder was removed",
            DefaultValue = "folder_removed",
            Placeholder = "folder_removed",
            Description = "Vora sweeps the folder and moves only the files that are really gone to Media Trash."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.TestEvents,
            Label = "Event names that are just a test",
            DefaultValue = "test",
            Description = "Answered and listed, and nothing else happens."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.FilePath,
            Label = "Where the file's full path is",
            DefaultValue = "path",
            Placeholder = "movieFile.path",
            ShowWhen = $"{MediaWebhookSettingKeys.Action}={MediaWebhookSettingKeys.IngestAction}",
            Description = "Dots go down into the JSON. Add [] to read every item of a list, such as episodeFiles[].path. Separate several fields with commas."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.AlsoRemovedPath,
            Label = "Where paths that were replaced are (optional)",
            Placeholder = "deletedFiles[].path",
            ShowWhen = $"{MediaWebhookSettingKeys.Action}={MediaWebhookSettingKeys.IngestAction}",
            Description = "Files named here are treated as removed on an added event, such as the old files an upgrade replaced. Vora checks each is really gone first."
        },
        new()
        {
            Key = MediaWebhookSettingKeys.FolderPath,
            Label = "Where the folder path is",
            DefaultValue = "folder",
            Placeholder = "series.path",
            ShowWhen = $"{MediaWebhookSettingKeys.Action}={MediaWebhookSettingKeys.IngestAction}",
            Description = "Read on a folder removed event."
        }
    };

    public LibraryKind? ResolveLibraryKind(IReadOnlyDictionary<string, string> settings)
    {
        if (Setting(settings, MediaWebhookSettingKeys.Action) == MediaWebhookSettingKeys.ScanAction) return null;
        return Enum.TryParse<LibraryKind>(Setting(settings, MediaWebhookSettingKeys.LibraryType), ignoreCase: true, out var kind) ? kind : null;
    }

    public MediaWebhookParseResult Parse(string body, IReadOnlyDictionary<string, string> settings)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var eventPath = Setting(settings, MediaWebhookSettingKeys.EventPath);
        var eventName = string.IsNullOrWhiteSpace(eventPath) ? string.Empty : JsonValuePaths.First(root, eventPath) ?? string.Empty;

        if (eventName.Length > 0 && Matches(Setting(settings, MediaWebhookSettingKeys.TestEvents), eventName))
        {
            return MediaWebhookParseResult.Test(null);
        }

        var kind = KindOf(settings, eventPath, eventName);
        if (kind == null)
        {
            return MediaWebhookParseResult.Ignored(eventName, null);
        }

        if (Setting(settings, MediaWebhookSettingKeys.Action) == MediaWebhookSettingKeys.ScanAction)
        {
            return MediaWebhookParseResult.Of(eventName, null, new[] { new MediaWebhookChange(MediaWebhookChangeKind.ScanLibrary, string.Empty) });
        }

        var files = JsonValuePaths.Values(root, Setting(settings, MediaWebhookSettingKeys.FilePath));
        var replaced = JsonValuePaths.Values(root, Setting(settings, MediaWebhookSettingKeys.AlsoRemovedPath));

        var changes = new List<MediaWebhookChange>();
        switch (kind.Value)
        {
            case MediaWebhookChangeKind.Added:
                changes.AddRange(files.Select(p => new MediaWebhookChange(MediaWebhookChangeKind.Added, p)));
                changes.AddRange(replaced.Where(p => !files.Contains(p)).Select(p => new MediaWebhookChange(MediaWebhookChangeKind.Removed, p)));
                break;
            case MediaWebhookChangeKind.Removed:
                changes.AddRange(files.Concat(replaced).Distinct(StringComparer.Ordinal).Select(p => new MediaWebhookChange(MediaWebhookChangeKind.Removed, p)));
                break;
            case MediaWebhookChangeKind.FolderRemoved:
                changes.AddRange(JsonValuePaths.Values(root, Setting(settings, MediaWebhookSettingKeys.FolderPath)).Select(p => new MediaWebhookChange(MediaWebhookChangeKind.FolderRemoved, p)));
                break;
        }

        return MediaWebhookParseResult.Of(eventName, null, changes);
    }

    private static MediaWebhookChangeKind? KindOf(IReadOnlyDictionary<string, string> settings, string? eventPath, string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventPath)) return MediaWebhookChangeKind.Added;
        if (Matches(Setting(settings, MediaWebhookSettingKeys.AddedEvents), eventName)) return MediaWebhookChangeKind.Added;
        if (Matches(Setting(settings, MediaWebhookSettingKeys.RemovedEvents), eventName)) return MediaWebhookChangeKind.Removed;
        if (Matches(Setting(settings, MediaWebhookSettingKeys.FolderRemovedEvents), eventName)) return MediaWebhookChangeKind.FolderRemoved;
        return null;
    }

    private static bool Matches(string? list, string eventName) =>
        !string.IsNullOrWhiteSpace(list)
        && list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => e.Equals(eventName, StringComparison.OrdinalIgnoreCase));

    private static string? Setting(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) ? value : null;
}
