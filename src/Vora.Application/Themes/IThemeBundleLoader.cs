using System.Text.Json;

namespace Vora.Application.Themes;

/// <summary>
/// Discovers and loads admin theme bundles from the filesystem.
///
/// A theme bundle is a folder at <c>&lt;plugins&gt;/themes/&lt;theme-id&gt;/</c>
/// containing:
///   - manifest.json   (required) — must include id, name, version, and tokens
///   - preview.png     (optional) — small preview image shown in the picker
///   - assets/         (optional) — folder of asset files referenced by manifest backgrounds
///
/// The folder name must match the manifest's id, otherwise the bundle is rejected.
/// (This prevents path-confusion between folder paths and theme identifiers.)
/// </summary>
public interface IThemeBundleLoader
{
    IReadOnlyList<ThemeBundle> GetBundles();
    ThemeBundle? Get(string id);
    /// <summary>Force a re-scan from disk. Returns the new bundle count.</summary>
    int Refresh();
}

public class ThemeBundleLoader : IThemeBundleLoader
{
    private readonly string _bundlesRootPath;
    private readonly object _lock = new();
    private Dictionary<string, ThemeBundle> _bundles = new(StringComparer.Ordinal);

    public ThemeBundleLoader(string bundlesRootPath)
    {
        _bundlesRootPath = bundlesRootPath;
        Refresh();
    }

    public IReadOnlyList<ThemeBundle> GetBundles()
    {
        lock (_lock)
        {
            return _bundles.Values.ToList();
        }
    }

    public ThemeBundle? Get(string id)
    {
        lock (_lock)
        {
            return _bundles.GetValueOrDefault(id);
        }
    }

    public int Refresh()
    {
        var loaded = new Dictionary<string, ThemeBundle>(StringComparer.Ordinal);

        if (!Directory.Exists(_bundlesRootPath))
        {
            // No themes/ folder yet — that's fine, plugin authors will create one.
            lock (_lock) { _bundles = loaded; }
            return 0;
        }

        foreach (var dir in Directory.GetDirectories(_bundlesRootPath))
        {
            var folderName = Path.GetFileName(dir);
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Console.WriteLine($"[Theme bundles] Skipping {folderName}: missing manifest.json");
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
                        Console.WriteLine($"[Theme bundles] Skipping {folderName}: duplicate theme id '{bundle.Id}'");
                        continue;
                    }
                    loaded[bundle.Id] = bundle;
                    Console.WriteLine($"[Theme bundles] Loaded '{bundle.Id}' ({bundle.Name})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Theme bundles] Failed to load {folderName}: {ex.Message}");
            }
        }

        lock (_lock) { _bundles = loaded; }
        return loaded.Count;
    }

    private static ThemeBundle? ParseAndValidate(string rawJson, string bundlePath, string folderName)
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

        // Folder name must match id to keep asset path resolution unambiguous.
        if (!string.Equals(folderName, id, StringComparison.Ordinal))
        {
            return Reject(folderName, $"folder name '{folderName}' must match manifest id '{id}'");
        }

        // Reject reserved built-in ids so plugin themes can't shadow them.
        if (id == "vora-default" || id == "vora-dark")
        {
            return Reject(folderName, $"id '{id}' is reserved for built-in themes");
        }

        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            return Reject(folderName, "missing or invalid 'tokens' block");
        }

        return new ThemeBundle
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

    private static ThemeBundle? Reject(string folderName, string reason)
    {
        Console.WriteLine($"[Theme bundles] Rejected {folderName}: {reason}");
        return null;
    }

    private static string? TryGetString(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
