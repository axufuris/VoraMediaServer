using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Arr;

public abstract class ArrWebhookProviderBase : IMediaWebhookProvider
{
    protected ArrWebhookProviderBase(string app)
    {
        App = app;
    }

    protected string App { get; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string SetupInstructions { get; }
    public virtual string? RequestServerProviderId => null;
    protected abstract LibraryKind Kind { get; }

    public string DefaultConnectionName => App;
    public string Version => "1.0.0";
    public string Type => "MediaWebhook";
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { Kind };

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Array.Empty<PluginSettingDefinitionDto>();

    public LibraryKind? ResolveLibraryKind(IReadOnlyDictionary<string, string> settings) => Kind;

    public abstract MediaWebhookParseResult Parse(string body, IReadOnlyDictionary<string, string> settings);

    protected static JsonDocument Open(string body) => JsonDocument.Parse(body);

    protected static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }
        return null;
    }

    protected static bool Flag(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == JsonValueKind.True;
        }
        return false;
    }

    protected static JsonElement Child(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }
        return default;
    }

    protected static IEnumerable<JsonElement> Items(JsonElement element, string name)
    {
        var child = Child(element, name);
        return child.ValueKind == JsonValueKind.Array ? child.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
    }

    protected static string? FilePath(JsonElement file) => Text(file, "path");

    protected static IEnumerable<string> Paths(IEnumerable<JsonElement> files) =>
        files.Select(FilePath).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);

    protected static IEnumerable<MediaWebhookChange> Additions(IEnumerable<string> added) =>
        added.Select(p => new MediaWebhookChange(MediaWebhookChangeKind.Added, p));

    protected static IEnumerable<MediaWebhookChange> Removals(IEnumerable<string> removed, IEnumerable<string> keep)
    {
        var kept = new HashSet<string>(keep, StringComparer.Ordinal);
        return removed
            .Where(p => !kept.Contains(p))
            .Select(p => new MediaWebhookChange(MediaWebhookChangeKind.Removed, p));
    }

    protected static IEnumerable<MediaWebhookChange> FolderRemoval(string? folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? Enumerable.Empty<MediaWebhookChange>()
            : new[] { new MediaWebhookChange(MediaWebhookChangeKind.FolderRemoved, folder) };

    protected static IEnumerable<MediaWebhookChange> Renames(IEnumerable<JsonElement> renamed)
    {
        foreach (var file in renamed)
        {
            var current = FilePath(file);
            var previous = Text(file, "previousPath");
            if (!string.IsNullOrWhiteSpace(current))
            {
                yield return new MediaWebhookChange(MediaWebhookChangeKind.Added, current);
            }

            if (!string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, current, StringComparison.Ordinal))
            {
                yield return new MediaWebhookChange(MediaWebhookChangeKind.Removed, previous);
            }
        }
    }
}
