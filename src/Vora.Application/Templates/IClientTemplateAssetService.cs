namespace Vora.Application.Templates;

public interface IClientTemplateAssetService
{
    string? ResolveAssetPath(string templateId, string assetPath);
}

public class ClientTemplateAssetService : IClientTemplateAssetService
{
    private readonly IClientTemplateBundleLoader _bundles;

    public ClientTemplateAssetService(IClientTemplateBundleLoader bundles)
    {
        _bundles = bundles;
    }

    public string? ResolveAssetPath(string templateId, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(templateId) || string.IsNullOrWhiteSpace(assetPath)) return null;

        var bundle = _bundles.Get(templateId);
        if (bundle == null) return null;

        var trimmed = assetPath.TrimStart('/', '\\');

        var assetsRoot = Path.GetFullPath(Path.Combine(bundle.BundlePath, "assets"));
        var combined = Path.GetFullPath(Path.Combine(assetsRoot, trimmed));

        var assetsRootWithSep = assetsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? assetsRoot
            : assetsRoot + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(assetsRootWithSep, StringComparison.Ordinal))
        {
            return null;
        }

        if (!File.Exists(combined)) return null;

        return combined;
    }
}
