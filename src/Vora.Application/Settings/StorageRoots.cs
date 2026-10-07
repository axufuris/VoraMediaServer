namespace Vora.Application.Settings;

public static class StorageRoots
{
    public static string CustomArtwork(StoragePathsOptions paths) =>
        Configured(paths.CustomArtwork) ?? Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

    public static string OriginalArtworkCache(StoragePathsOptions paths) =>
        Configured(paths.OriginalArtworkCache) ?? Path.Combine(AppContext.BaseDirectory, "Storage", "OriginalArtworkCache");

    public static string UserImages(StoragePathsOptions paths) =>
        Configured(paths.UserImages) ?? Path.Combine(AppContext.BaseDirectory, "Users");

    public static string Plugins(StoragePathsOptions paths) =>
        Configured(paths.Plugins) ?? Path.Combine(AppContext.BaseDirectory, "Plugins");

    public static string AudioFingerprints(StoragePathsOptions paths) =>
        Configured(paths.AudioFingerprints) ?? Path.Combine(AppContext.BaseDirectory, "data", "audiofingerprints");

    public static string IptvDvr(StoragePathsOptions paths, string? configuredInSettings) =>
        Configured(configuredInSettings) ?? Configured(paths.IptvDvr) ?? "/app/data/iptv/dvr";

    private static string? Configured(string? path) => string.IsNullOrWhiteSpace(path) ? null : path;
}
