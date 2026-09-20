namespace Vora.Application.Themes;

/// <summary>
/// Aggregates all admin themes known to the server — both built-in themes
/// hardcoded here and plugin-shipped themes loaded by IThemeBundleLoader.
/// </summary>
public interface IThemeRegistry
{
    IReadOnlyList<ThemeMetaVM> GetAll();
    ThemeMetaVM? Get(string id);
    bool Exists(string id);
    /// <summary>The raw manifest JSON for a plugin theme. Built-in themes return null
    /// because the frontend bundles their manifests at build time.</summary>
    string? GetManifestJson(string id);
}

public class ThemeRegistry : IThemeRegistry
{
    private static readonly IReadOnlyList<ThemeMetaVM> BuiltIn = new List<ThemeMetaVM>
    {
        new()
        {
            Id = "vora-dark",
            Name = "Vora Dark",
            Version = "1.0.0",
            Author = "Vora",
            Description = "Cool zinc neutrals with a brighter amber accent and a subtle canvas glow.",
            IsBuiltIn = true,
        },
        new()
        {
            Id = "vora-light",
            Name = "Vora Light",
            Version = "1.0.0",
            Author = "Vora",
            Description = "Warm neutrals with an amber accent. Editorial and quiet.",
            IsBuiltIn = true,
        },
        new()
        {
            Id = "vora-ocean",
            Name = "Vora Ocean",
            Version = "1.0.0",
            Author = "Vora",
            Description = "Deep navy surfaces with a teal accent. Cooler, more technical.",
            IsBuiltIn = true,
        },
    };

    private readonly IThemeBundleLoader _bundles;

    public ThemeRegistry(IThemeBundleLoader bundles)
    {
        _bundles = bundles;
    }

    public IReadOnlyList<ThemeMetaVM> GetAll()
    {
        // Built-ins first, then plugin themes alphabetically. Plugin themes
        // can't shadow built-in ids (the bundle loader rejects them), so
        // there's no merge conflict to resolve.
        var result = new List<ThemeMetaVM>(BuiltIn);
        var bundleMetas = _bundles.GetBundles()
            .Select(b => new ThemeMetaVM
            {
                Id = b.Id,
                Name = b.Name,
                Version = b.Version,
                Author = b.Author,
                Description = b.Description,
                Preview = b.PreviewRelativePath is null ? null : $"/api/admin/themes/{b.Id}/assets/{b.PreviewRelativePath}",
                IsBuiltIn = false,
            })
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
        result.AddRange(bundleMetas);
        return result;
    }

    public ThemeMetaVM? Get(string id)
    {
        var builtIn = BuiltIn.FirstOrDefault(t => t.Id.Equals(id, StringComparison.Ordinal));
        if (builtIn != null) return builtIn;

        var bundle = _bundles.Get(id);
        if (bundle == null) return null;
        return new ThemeMetaVM
        {
            Id = bundle.Id,
            Name = bundle.Name,
            Version = bundle.Version,
            Author = bundle.Author,
            Description = bundle.Description,
            Preview = bundle.PreviewRelativePath is null ? null : $"/api/admin/themes/{bundle.Id}/assets/{bundle.PreviewRelativePath}",
            IsBuiltIn = false,
        };
    }

    public bool Exists(string id) => Get(id) is not null;

    public string? GetManifestJson(string id) => _bundles.Get(id)?.RawManifestJson;
}
