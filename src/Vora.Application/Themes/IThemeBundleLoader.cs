using System.Text.Json;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<ThemeBundleLoader> _logger;
    private readonly object _lock = new();
    private Dictionary<string, ThemeBundle> _bundles = new(StringComparer.Ordinal);

    public ThemeBundleLoader(string bundlesRootPath, ILogger<ThemeBundleLoader> logger)
    {
        _bundlesRootPath = bundlesRootPath;
        _logger = logger;
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
                _logger.LogWarning("Skipping theme bundle {FolderName}: missing manifest.json", folderName);
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var bundle = ParseAndValidate(json, dir, folderName, _logger);
                if (bundle != null)
                {
                    if (loaded.ContainsKey(bundle.Id))
                    {
                        _logger.LogWarning("Skipping theme bundle {FolderName}: duplicate theme id '{BundleId}'", folderName, bundle.Id);
                        continue;
                    }
                    loaded[bundle.Id] = bundle;
                    _logger.LogInformation("Loaded theme bundle '{BundleId}' ({BundleName})", bundle.Id, bundle.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load theme bundle {FolderName}", folderName);
            }
        }

        lock (_lock) { _bundles = loaded; }
        return loaded.Count;
    }

    private static ThemeBundle? ParseAndValidate(string rawJson, string bundlePath, string folderName, ILogger logger)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object) return Reject(folderName, "root is not an object", logger);

        var id = TryGetString(root, "id");
        var name = TryGetString(root, "name");
        var version = TryGetString(root, "version");

        if (string.IsNullOrWhiteSpace(id)) return Reject(folderName, "missing 'id'", logger);
        if (string.IsNullOrWhiteSpace(name)) return Reject(folderName, "missing 'name'", logger);
        if (string.IsNullOrWhiteSpace(version)) return Reject(folderName, "missing 'version'", logger);

        if (!string.Equals(folderName, id, StringComparison.Ordinal))
        {
            return Reject(folderName, $"folder name '{folderName}' must match manifest id '{id}'", logger);
        }

        if (id == "vora-default" || id == "vora-dark")
        {
            return Reject(folderName, $"id '{id}' is reserved for built-in themes", logger);
        }

        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            return Reject(folderName, "missing or invalid 'tokens' block", logger);
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

    private static ThemeBundle? Reject(string folderName, string reason, ILogger logger)
    {
        logger.LogWarning("Rejected theme bundle {FolderName}: {Reason}", folderName, reason);
        return null;
    }

    private static string? TryGetString(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
