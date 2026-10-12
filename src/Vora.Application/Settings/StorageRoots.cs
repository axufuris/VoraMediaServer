using System.Collections.Concurrent;

namespace Vora.Application.Settings;

public static class StorageRoots
{
    private const string ShippedDataFolderName = "data";

    private static readonly ConcurrentDictionary<string, string> Resolved = new(StringComparer.Ordinal);

    public static string CustomArtwork(StoragePathsOptions paths) => CustomArtwork(paths, AppContext.BaseDirectory);

    public static string OriginalArtworkCache(StoragePathsOptions paths) => OriginalArtworkCache(paths, AppContext.BaseDirectory);

    public static string UserImages(StoragePathsOptions paths) => UserImages(paths, AppContext.BaseDirectory);

    public static string Plugins(StoragePathsOptions paths) => Plugins(paths, AppContext.BaseDirectory);

    public static string AudioFingerprints(StoragePathsOptions paths) => AudioFingerprints(paths, AppContext.BaseDirectory);

    public static string EpgCache(StoragePathsOptions paths) => EpgCache(paths, AppContext.BaseDirectory);

    public static string IptvDvr(StoragePathsOptions paths, string? configuredInSettings) =>
        IptvDvr(paths, configuredInSettings, AppContext.BaseDirectory);

    public static string VideoThumbnails(StoragePathsOptions paths) => VideoThumbnails(paths, AppContext.BaseDirectory);

    public static string Logs(StoragePathsOptions paths) => Logs(paths, AppContext.BaseDirectory);

    public static string Backups(StoragePathsOptions paths) => Backups(paths, AppContext.BaseDirectory);

    public static string DataProtection(StoragePathsOptions paths) => DataProtection(paths, AppContext.BaseDirectory);

    public static string? DataFolder(StoragePathsOptions paths) => DataFolder(paths, AppContext.BaseDirectory);

    public static IReadOnlyList<(string Setting, string Folder)> All(StoragePathsOptions paths, string? dvrFolderInSettings = null) =>
        All(paths, dvrFolderInSettings, AppContext.BaseDirectory);

    internal static string CustomArtwork(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.CustomArtwork) ?? InDataFolder(paths, appFolder, Path.Combine("Storage", "CustomArtwork"), "custom_artwork");

    internal static string OriginalArtworkCache(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.OriginalArtworkCache) ?? InDataFolder(paths, appFolder, Path.Combine("Storage", "OriginalArtworkCache"), "original_artwork_cache");

    internal static string UserImages(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.UserImages) ?? InDataFolder(paths, appFolder, "Users", "users");

    internal static string Plugins(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.Plugins) ?? InDataFolder(paths, appFolder, "Plugins", "plugins");

    internal static string AudioFingerprints(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.AudioFingerprints) ?? InDataFolder(paths, appFolder, Path.Combine("data", "audiofingerprints"), "audiofingerprints");

    internal static string EpgCache(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.EpgCache) ?? InDataFolder(paths, appFolder, Path.Combine("Storage", "Iptv"), Path.Combine("iptv", "epg_cache"));

    internal static string IptvDvr(StoragePathsOptions paths, string? configuredInSettings, string appFolder) =>
        Configured(configuredInSettings)
        ?? Configured(paths.IptvDvr)
        ?? InDataFolder(paths, appFolder, Path.Combine("data", "iptv", "dvr"), Path.Combine("iptv", "dvr"));

    internal static string VideoThumbnails(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.VideoThumbnails) ?? InDataFolder(paths, appFolder, "video-thumbnails", "video-thumbnails");

    internal static string Logs(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.Logs) ?? InDataFolder(paths, appFolder, "logs", "logs");

    internal static string Backups(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.Backups) ?? InDataFolder(paths, appFolder, "backups", "backups");

    internal static string DataProtection(StoragePathsOptions paths, string appFolder) =>
        Configured(paths.DataProtection) ?? InDataFolder(paths, appFolder, "DataProtectionKeys", "dp-keys");

    internal static string? DataFolder(StoragePathsOptions paths, string appFolder) =>
        ConfiguredDataFolder(paths) ?? ShippedDataFolder(appFolder);

    internal static IReadOnlyList<(string Setting, string Folder)> All(StoragePathsOptions paths, string? dvrFolderInSettings, string appFolder) =>
    [
        (nameof(StoragePathsOptions.CustomArtwork), CustomArtwork(paths, appFolder)),
        (nameof(StoragePathsOptions.OriginalArtworkCache), OriginalArtworkCache(paths, appFolder)),
        (nameof(StoragePathsOptions.UserImages), UserImages(paths, appFolder)),
        (nameof(StoragePathsOptions.Plugins), Plugins(paths, appFolder)),
        (nameof(StoragePathsOptions.EpgCache), EpgCache(paths, appFolder)),
        (nameof(StoragePathsOptions.IptvDvr), IptvDvr(paths, dvrFolderInSettings, appFolder)),
        (nameof(StoragePathsOptions.VideoThumbnails), VideoThumbnails(paths, appFolder)),
        (nameof(StoragePathsOptions.AudioFingerprints), AudioFingerprints(paths, appFolder)),
        (nameof(StoragePathsOptions.Logs), Logs(paths, appFolder)),
        (nameof(StoragePathsOptions.Backups), Backups(paths, appFolder)),
        (nameof(StoragePathsOptions.DataProtection), DataProtection(paths, appFolder)),
    ];

    internal static string InDataFolder(StoragePathsOptions paths, string appFolder, string nameInAppFolder, string nameInDataFolder)
    {
        var besideConfiguredFolders = ConfiguredDataFolder(paths);

        return Resolved.GetOrAdd($"{appFolder}\n{nameInAppFolder}\n{nameInDataFolder}\n{besideConfiguredFolders}", _ =>
        {
            var inAppFolder = Path.Combine(appFolder, nameInAppFolder);
            if (Directory.Exists(inAppFolder)) return inAppFolder;

            var dataFolder = besideConfiguredFolders ?? ShippedDataFolder(appFolder);
            return dataFolder == null ? inAppFolder : Path.Combine(dataFolder, nameInDataFolder);
        });
    }

    private static string? ConfiguredDataFolder(StoragePathsOptions paths)
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

    private static string? ShippedDataFolder(string appFolder)
    {
        var shipped = Path.Combine(appFolder, ShippedDataFolderName);
        return Directory.Exists(shipped) ? shipped : null;
    }

    private static string? Configured(string? path) => string.IsNullOrWhiteSpace(path) ? null : path;
}
