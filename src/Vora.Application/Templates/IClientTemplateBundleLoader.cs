using System.Text.Json;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<ClientTemplateBundleLoader> _logger;
    private readonly object _lock = new();
    private Dictionary<string, TemplateBundle> _bundles = new(StringComparer.Ordinal);

    public ClientTemplateBundleLoader(string bundlesRootPath, ILogger<ClientTemplateBundleLoader> logger)
    {
        _bundlesRootPath = bundlesRootPath;
        _logger = logger;
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
                _logger.LogWarning("Skipping client template {FolderName}: missing manifest.json", folderName);
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
                        _logger.LogWarning("Skipping client template {FolderName}: duplicate template id '{BundleId}'", folderName, bundle.Id);
                        continue;
                    }
                    loaded[bundle.Id] = bundle;
                    _logger.LogInformation("Loaded client template '{BundleId}' ({BundleName})", bundle.Id, bundle.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load client template {FolderName}", folderName);
            }
        }

        lock (_lock) { _bundles = loaded; }
        return loaded.Count;
    }

    private static TemplateBundle? ParseAndValidate(string rawJson, string bundlePath, string folderName, ILogger logger)
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

        if (ReservedIds.Contains(id!))
        {
            return Reject(folderName, $"id '{id}' is reserved for built-in templates", logger);
        }

        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            return Reject(folderName, "missing or invalid 'tokens' block", logger);
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

    private static TemplateBundle? Reject(string folderName, string reason, ILogger logger)
    {
        logger.LogWarning("Rejected client template {FolderName}: {Reason}", folderName, reason);
        return null;
    }

    private static string? TryGetString(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
