namespace Vora.Application.Themes;

/// <summary>
/// Resolves an asset path inside a theme bundle to an absolute filesystem path,
/// with path-traversal protection. Asset names like <c>../../etc/passwd</c> are
/// rejected; only paths that resolve inside the bundle directory are returned.
/// </summary>
public interface IThemeAssetService
{
    /// <summary>
    /// Returns the absolute path if the resolved file exists inside the bundle,
    /// or null if the theme/asset doesn't exist or the path tries to escape the bundle.
    /// </summary>
    string? ResolveAssetPath(string themeId, string assetPath);
}

public class ThemeAssetService : IThemeAssetService
{
    private readonly IThemeBundleLoader _bundles;

    public ThemeAssetService(IThemeBundleLoader bundles)
    {
        _bundles = bundles;
    }

    public string? ResolveAssetPath(string themeId, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(themeId) || string.IsNullOrWhiteSpace(assetPath)) return null;

        var bundle = _bundles.Get(themeId);
        if (bundle == null) return null;

        // Strip any leading slashes; the path is always relative to the bundle's
        // assets/ folder, NOT the bundle root. This keeps manifest authors from
        // having to write "assets/" in every image reference and prevents access
        // to manifest.json or other top-level bundle files via the asset URL.
        var trimmed = assetPath.TrimStart('/', '\\');

        var assetsRoot = Path.GetFullPath(Path.Combine(bundle.BundlePath, "assets"));
        var combined = Path.GetFullPath(Path.Combine(assetsRoot, trimmed));

        var assetsRootWithSep = assetsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? assetsRoot
            : assetsRoot + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(assetsRootWithSep, StringComparison.Ordinal))
        {
            // Path tried to escape the assets/ folder (e.g. ../manifest.json).
            return null;
        }

        if (!File.Exists(combined)) return null;

        return combined;
    }
}
