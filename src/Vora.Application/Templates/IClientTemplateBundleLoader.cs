using System.Text.Json;

namespace Vora.Application.Templates;

public interface IClientTemplateBundleLoader
{
    IReadOnlyList<TemplateBundle> GetBundles();
    TemplateBundle? Get(string id);
    int Refresh();
}

public class ClientTemplateBundleLoader : IClientTemplateBundleLoader
{
    private static readonly HashSet<string> ReservedIds = new(StringComparer.Ordinal)
    {
        "vora-cinema",
        "vora-noir",
        "vora-velvet",
        "vora-aurora"
    };

    private readonly string _bundlesRootPath;
    private readonly object _lock = new();
    private Dictionary<string, TemplateBundle> _bundles = new(StringComparer.Ordinal);

    public ClientTemplateBundleLoader(string bundlesRootPath)
    {
        _bundlesRootPath = bundlesRootPath;
        Refresh();
    }

    public IReadOnlyList<TemplateBundle> GetBundles()
    {
        lock (_lock)
        {
            return _bundles.Values.ToList();
        }
    }

    public TemplateBundle? Get(string id)
    {
        lock (_lock)
        {
            return _bundles.GetValueOrDefault(id);
        }
    }

    public int Refresh()
    {
        var loaded = new Dictionary<string, TemplateBundle>(StringComparer.Ordinal);

        if (!Directory.Exists(_bundlesRootPath))
        {
            lock (_lock) { _bundles = loaded; }
            return 0;
        }

        foreach (var dir in Directory.GetDirectories(_bundlesRootPath))
        {
            var folderName = Path.GetFileName(dir);
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Console.WriteLine($"[Client templates] Skipping {folderName}: missing manifest.json");
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var bundle = ParseAndValidate(json, dir, folderName);
                if (bundle != null)
                {
                    if (loaded.ContainsKey(bundle.Id))
                    {
                        Console.WriteLine($"[Client templates] Skipping {folderName}: duplicate template id '{bundle.Id}'");
                        continue;
                    }
                    loaded[bundle.Id] = bundle;
                    Console.WriteLine($"[Client templates] Loaded '{bundle.Id}' ({bundle.Name})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client templates] Failed to load {folderName}: {ex.Message}");
            }
        }

        lock (_lock) { _bundles = loaded; }
        return loaded.Count;
    }

    private static TemplateBundle? ParseAndValidate(string rawJson, string bundlePath, string folderName)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object) return Reject(folderName, "root is not an object");

        var id = TryGetString(root, "id");
        var name = TryGetString(root, "name");
        var version = TryGetString(root, "version");

        if (string.IsNullOrWhiteSpace(id)) return Reject(folderName, "missing 'id'");
        if (string.IsNullOrWhiteSpace(name)) return Reject(folderName, "missing 'name'");
        if (string.IsNullOrWhiteSpace(version)) return Reject(folderName, "missing 'version'");

        if (!string.Equals(folderName, id, StringComparison.Ordinal))
        {
            return Reject(folderName, $"folder name '{folderName}' must match manifest id '{id}'");
        }

        if (ReservedIds.Contains(id!))
        {
            return Reject(folderName, $"id '{id}' is reserved for built-in templates");
        }

        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            return Reject(folderName, "missing or invalid 'tokens' block");
        }

        return new TemplateBundle
        {
            Id = id!,
            Name = name!,
            Version = version!,
            Author = TryGetString(root, "author"),
            Description = TryGetString(root, "description"),
            PreviewRelativePath = TryGetString(root, "preview"),
            BundlePath = bundlePath,
            RawManifestJson = rawJson,
        };
    }

    private static TemplateBundle? Reject(string folderName, string reason)
    {
        Console.WriteLine($"[Client templates] Rejected {folderName}: {reason}");
        return null;
    }

    private static string? TryGetString(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
