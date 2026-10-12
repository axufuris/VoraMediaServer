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

    public static string VideoThumbnails(StoragePathsOptions paths) =>
        Configured(paths.VideoThumbnails) ?? InDataFolder(paths, AppContext.BaseDirectory, "video-thumbnails", "video-thumbnails");

    public static string Logs(StoragePathsOptions paths) =>
        Configured(paths.Logs) ?? InDataFolder(paths, AppContext.BaseDirectory, "logs", "logs");

    public static string Backups(StoragePathsOptions paths) =>
        Configured(paths.Backups) ?? InDataFolder(paths, AppContext.BaseDirectory, "backups", "backups");

    public static string DataProtection(StoragePathsOptions paths) =>
        Configured(paths.DataProtection) ?? InDataFolder(paths, AppContext.BaseDirectory, "DataProtectionKeys", "dp-keys");

    public static string? DataFolder(StoragePathsOptions paths)
    {
        string?[] besideOneAnother =
        [
            paths.CustomArtwork, paths.OriginalArtworkCache, paths.UserImages, paths.Plugins, paths.Subtitles,
            paths.VideoThumbnails, paths.Backups, paths.Logs, paths.DataProtection, paths.Metadata, paths.AudioFingerprints
        ];

        foreach (var path in besideOneAnother)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            var parent = Path.GetDirectoryName(path.TrimEnd('/', '\\'));
            if (!string.IsNullOrWhiteSpace(parent)) return parent;
        }

        return null;
    }

    internal static string InDataFolder(StoragePathsOptions paths, string appFolder, string nameInAppFolder, string nameInDataFolder)
    {
        var inAppFolder = Path.Combine(appFolder, nameInAppFolder);
        if (Directory.Exists(inAppFolder)) return inAppFolder;

        var dataFolder = DataFolder(paths);
        return dataFolder == null ? inAppFolder : Path.Combine(dataFolder, nameInDataFolder);
    }

    private static string? Configured(string? path) => string.IsNullOrWhiteSpace(path) ? null : path;
}
