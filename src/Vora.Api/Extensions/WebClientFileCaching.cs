namespace Vora.Api.Extensions;

public static class WebClientFileCaching
{
    public const string AppShell = "no-cache";
    public const string HashedAsset = "public, max-age=31536000, immutable";

    private static readonly HashSet<string> AppShellFiles = new(StringComparer.OrdinalIgnoreCase) { "index.html", "sw.js", "manifest.webmanifest" };
    private const string HashedAssetFolder = "/assets";

    public static StaticFileOptions Options() => new()
    {
        OnPrepareResponse = file =>
        {
            var cacheControl = CacheControlFor(file.Context.Request.Path, file.File.Name);
            if (cacheControl != null) file.Context.Response.Headers.CacheControl = cacheControl;
        }
    };

    public static string? CacheControlFor(PathString requestPath, string fileName)
    {
        if (AppShellFiles.Contains(fileName)) return AppShell;
        return requestPath.StartsWithSegments(HashedAssetFolder, StringComparison.OrdinalIgnoreCase) ? HashedAsset : null;
    }
}
