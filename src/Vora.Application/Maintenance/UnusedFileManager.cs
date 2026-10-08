using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.Backups;
using Vora.Application.Posters;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Subtitles;
using Vora.Application.Thumbnails;

namespace Vora.Application.Maintenance;

public interface IUnusedFileManager
{
    Task<UnusedFilesReportVM> ScanAsync(CancellationToken cancellationToken = default);
    Task<UnusedFilesReportVM> RemoveAsync(CancellationToken cancellationToken = default);
}

public sealed partial class UnusedFileManager : IUnusedFileManager
{
    public const string ArtworkUrlPrefix = "/api/artwork/custom/";
    public const string ProfileImageUrlPrefix = "/api/users/images/custom/";
    public static readonly TimeSpan RecentlyWritten = TimeSpan.FromHours(1);
    public static readonly TimeSpan UnfinishedAfter = TimeSpan.FromDays(1);
    private const int ExampleCount = 5;

    private readonly IStorageReferenceRepository _references;
    private readonly ISystemSettingsRepository _settings;
    private readonly IVideoThumbnailStorageService _videoThumbnails;
    private readonly ISubtitleExtractionService _subtitleCache;
    private readonly IBackupManager _backups;
    private readonly IArtworkThumbnailService _artworkCache;
    private readonly StoragePathsOptions _paths;
    private readonly ILogger<UnusedFileManager> _logger;

    public UnusedFileManager(
        IStorageReferenceRepository references,
        ISystemSettingsRepository settings,
        IVideoThumbnailStorageService videoThumbnails,
        ISubtitleExtractionService subtitleCache,
        IBackupManager backups,
        IArtworkThumbnailService artworkCache,
        IOptions<StoragePathsOptions> paths,
        ILogger<UnusedFileManager> logger)
    {
        _references = references;
        _settings = settings;
        _videoThumbnails = videoThumbnails;
        _subtitleCache = subtitleCache;
        _backups = backups;
        _artworkCache = artworkCache;
        _paths = paths.Value;
        _logger = logger;
    }

    public async Task<UnusedFilesReportVM> ScanAsync(CancellationToken cancellationToken = default)
    {
        var groups = await FindAsync(cancellationToken);
        return Report(groups, removed: false);
    }

    public async Task<UnusedFilesReportVM> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var groups = await FindAsync(cancellationToken);
        var removed = new List<UnusedGroup>();

        foreach (var group in groups)
        {
            if (!group.Removable)
            {
                removed.Add(group);
                continue;
            }

            var deleted = new UnusedGroup(group.Kind, group.Folder, Removable: true);
            foreach (var entry in group.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryDelete(entry)) continue;
                deleted.Entries.Add(entry);
                if (group.Kind == UnusedFileKind.Artwork) _artworkCache.RemoveThumbnailsForSource(ArtworkUrlPrefix + Path.GetFileName(entry.Path));
            }

            if (group.Kind is UnusedFileKind.ScrubThumbnails or UnusedFileKind.DownloadedSubtitles)
            {
                RemoveEmptyFolders(group.Folder);
            }
            removed.Add(deleted);
        }

        var report = Report(removed, removed: true);
        _logger.LogInformation("Removed {Files} unused file(s), {Bytes} bytes.", report.RemovableFiles, report.RemovableBytes);
        return report;
    }

    private async Task<List<UnusedGroup>> FindAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetSettingsAsync();
        var now = DateTime.UtcNow;
        var recentCutoff = now - RecentlyWritten;
        var unfinishedCutoff = now - UnfinishedAfter;

        var artworkRoot = StorageRoots.CustomArtwork(_paths);
        var profileRoot = StorageRoots.UserImages(_paths);
        var references = await _references.FindReferencedNamesAsync(
            new[] { ArtworkUrlPrefix, FolderPrefix(artworkRoot), ProfileImageUrlPrefix, FolderPrefix(profileRoot) },
            cancellationToken);
        var partIds = await _references.GetMediaPartIdsAsync(cancellationToken);
        var subtitleCacheRoot = _subtitleCache.GetCacheDirectory(StreamManager.ResolveTempDirectory(settings));

        var subtitleRoot = SubtitleStorePath.Resolve(_paths);
        var originalRoot = StorageRoots.OriginalArtworkCache(_paths);
        var pluginRoot = StorageRoots.Plugins(_paths);
        var backupRoot = await _backups.GetEffectiveDirectoryAsync(cancellationToken);
        var recordingRoot = StorageRoots.IptvDvr(_paths, settings.DvrStoragePath);
        var allRoots = new[]
        {
            artworkRoot, profileRoot, _videoThumbnails.RootDirectory, subtitleRoot, originalRoot, pluginRoot, backupRoot, recordingRoot,
            StreamManager.ResolveTempDirectory(settings), StorageRoots.AudioFingerprints(_paths),
            _paths.Logs, _paths.DataProtection, _paths.EpgCache, _paths.Metadata
        };

        var groups = new List<UnusedGroup>();
        Add(groups, UnusedFileKind.Artwork, artworkRoot, () =>
            UnreferencedTopLevelFiles(artworkRoot, ArtworkFileName(), Referenced(references, ArtworkUrlPrefix, FolderPrefix(artworkRoot)), recentCutoff));
        Add(groups, UnusedFileKind.ProfilePictures, profileRoot, () =>
            UnreferencedTopLevelFiles(profileRoot, ProfileFileName(), Referenced(references, ProfileImageUrlPrefix, FolderPrefix(profileRoot)), recentCutoff));

        var itemIds = await _references.GetMediaItemIdsAsync(cancellationToken);
        Add(groups, UnusedFileKind.ScrubThumbnails, _videoThumbnails.RootDirectory, () =>
            UnusedThumbnailFolders(_videoThumbnails.RootDirectory, itemIds, partIds, recentCutoff));

        var subtitleReferences = await _references.GetSubtitleFileReferencesAsync(cancellationToken);
        Add(groups, UnusedFileKind.DownloadedSubtitles, subtitleRoot, () =>
            HoldsOtherStorage(subtitleRoot, allRoots) ? Nothing() : UnusedDownloadedSubtitles(subtitleRoot, subtitleReferences, recentCutoff));

        var originalUrls = await _references.GetOriginalPosterUrlsAsync(cancellationToken);
        Add(groups, UnusedFileKind.OriginalArtworkCache, originalRoot, () =>
            HoldsOtherStorage(originalRoot, allRoots) ? Nothing() : UnreferencedTopLevelFiles(originalRoot, AnyFileName(), OriginalArtworkNames(originalUrls), recentCutoff));

        Add(groups, UnusedFileKind.SubtitleCache, subtitleCacheRoot, () =>
            UnusedSubtitleCacheFiles(subtitleCacheRoot, partIds, recentCutoff));

        Add(groups, UnusedFileKind.RemovedPlugins, pluginRoot, () =>
            Files(pluginRoot, "*.deleted", SearchOption.AllDirectories, recentCutoff));

        Add(groups, UnusedFileKind.UnfinishedFiles, string.Empty, () =>
            Files(Path.Combine(artworkRoot, "imagecache"), "*.tmp", SearchOption.AllDirectories, unfinishedCutoff)
                .Concat(Files(backupRoot, "*.tmp", SearchOption.TopDirectoryOnly, unfinishedCutoff))
                .Concat(Files(StorageRoots.AudioFingerprints(_paths), "fp_*.wav", SearchOption.TopDirectoryOnly, unfinishedCutoff))
                .Concat(Files(subtitleCacheRoot, "*.tmp", SearchOption.TopDirectoryOnly, unfinishedCutoff)));

        var recordingPaths = await _references.GetRecordingFilePathsAsync(cancellationToken);
        Add(groups, UnusedFileKind.Recordings, recordingRoot, () =>
            UnknownRecordings(recordingRoot, recordingPaths, recentCutoff), removable: false);

        return groups;
    }

    private void Add(List<UnusedGroup> groups, UnusedFileKind kind, string folder, Func<IEnumerable<UnusedEntry>> find, bool removable = true)
    {
        var group = new UnusedGroup(kind, folder, removable);
        try
        {
            group.Entries.AddRange(find());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not look for unused files of kind {Kind} in {Folder}.", kind, folder);
        }
        groups.Add(group);
    }

    internal static bool HoldsOtherStorage(string root, IEnumerable<string?> allRoots)
    {
        var folder = FolderPrefix(root);
        return allRoots
            .Where(other => !string.IsNullOrWhiteSpace(other))
            .Select(other => FolderPrefix(other ?? string.Empty))
            .Any(other => !string.Equals(other, folder, StringComparison.OrdinalIgnoreCase)
                && other.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<UnusedEntry> Nothing() => Enumerable.Empty<UnusedEntry>();

    internal static string FolderPrefix(string folder) =>
        Path.GetFullPath(folder).Replace('\\', '/').TrimEnd('/') + "/";

    internal static HashSet<string> Referenced(Dictionary<string, HashSet<string>> references, params string[] prefixes)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in prefixes)
        {
            if (!references.TryGetValue(prefix, out var values)) continue;
            foreach (var value in values)
            {
                var decoded = Uri.UnescapeDataString(value);
                names.Add(value);
                names.Add(value.Split('/')[0]);
                names.Add(decoded);
                names.Add(decoded.Split('/')[0]);
            }
        }
        return names;
    }

    internal static HashSet<string> OriginalArtworkNames(IEnumerable<string> urls)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            if (PosterOverlayManager.OriginalArtworkCacheFileName(url) is string name) names.Add(name);
        }
        return names;
    }

    private static IEnumerable<UnusedEntry> UnreferencedTopLevelFiles(string root, Regex madeByVora, HashSet<string> referenced, DateTime recentCutoff)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            if (info.Name.StartsWith('.') || !madeByVora.IsMatch(info.Name)) continue;
            if (referenced.Contains(info.Name) || info.LastWriteTimeUtc > recentCutoff) continue;
            yield return new UnusedEntry(path, IsFolder: false, 1, info.Length);
        }
    }

    private static IEnumerable<UnusedEntry> UnusedThumbnailFolders(string root, HashSet<Guid> itemIds, HashSet<Guid> partIds, DateTime recentCutoff)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var shard in Directory.EnumerateDirectories(root))
        {
            if (Path.GetFileName(shard).Length != 2) continue;

            foreach (var itemFolder in Directory.EnumerateDirectories(shard))
            {
                if (!Guid.TryParseExact(Path.GetFileName(itemFolder), "N", out var itemId)) continue;

                if (!itemIds.Contains(itemId))
                {
                    if (Folder(itemFolder, recentCutoff) is UnusedEntry item) yield return item;
                    continue;
                }

                foreach (var partFolder in Directory.EnumerateDirectories(itemFolder))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(partFolder), "N", out var partId) || partIds.Contains(partId)) continue;
                    if (Folder(partFolder, recentCutoff) is UnusedEntry part) yield return part;
                }
            }
        }
    }

    private static IEnumerable<UnusedEntry> UnusedDownloadedSubtitles(string root, SubtitleFileReferences references, DateTime recentCutoff)
    {
        if (!Directory.Exists(root)) yield break;

        var paths = new HashSet<string>(references.FilePaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        var itemFiles = Directory.EnumerateDirectories(root)
            .Where(shard => Path.GetFileName(shard).Length == 2)
            .SelectMany(Directory.EnumerateDirectories)
            .Where(item => Guid.TryParseExact(Path.GetFileName(item), "N", out _))
            .SelectMany(item => Directory.EnumerateFiles(item, "*", SearchOption.TopDirectoryOnly));

        foreach (var path in itemFiles)
        {
            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > recentCutoff || paths.Contains(NormalizePath(path))) continue;
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var trackId) && references.TrackIds.Contains(trackId)) continue;
            yield return new UnusedEntry(path, IsFolder: false, 1, info.Length);
        }
    }

    private static IEnumerable<UnusedEntry> UnusedSubtitleCacheFiles(string root, HashSet<Guid> partIds, DateTime recentCutoff)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".failed", StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileNameWithoutExtension(path).Split('_') is not [var part, _, _] || !Guid.TryParse(part, out var partId)) continue;
            if (partIds.Contains(partId)) continue;

            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > recentCutoff) continue;
            yield return new UnusedEntry(path, IsFolder: false, 1, info.Length);
        }
    }

    private static IEnumerable<UnusedEntry> UnknownRecordings(string root, IEnumerable<string> recordingPaths, DateTime recentCutoff)
    {
        if (!Directory.Exists(root)) yield break;

        var known = new HashSet<string>(recordingPaths.Select(StemOf), StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            if (!RecordingName().IsMatch(Path.GetFileName(path)) || known.Contains(StemOf(path))) continue;

            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > recentCutoff) continue;
            yield return new UnusedEntry(path, IsFolder: false, 1, info.Length);
        }
    }

    private static IEnumerable<UnusedEntry> Files(string root, string pattern, SearchOption depth, DateTime olderThan)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root, pattern, depth))
        {
            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > olderThan) continue;
            yield return new UnusedEntry(path, IsFolder: false, 1, info.Length);
        }
    }

    private static UnusedEntry? Folder(string path, DateTime recentCutoff)
    {
        var files = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        if (files.Any(f => f.LastWriteTimeUtc > recentCutoff)) return null;
        return new UnusedEntry(path, IsFolder: true, files.Count, files.Sum(f => f.Length));
    }

    private bool TryDelete(UnusedEntry entry)
    {
        try
        {
            if (entry.IsFolder) Directory.Delete(entry.Path, recursive: true);
            else File.Delete(entry.Path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove unused {Path}.", entry.Path);
            return false;
        }
    }

    private void RemoveEmptyFolders(string root)
    {
        if (!Directory.Exists(root)) return;

        foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(f => f.Length).ToList())
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not remove empty folder {Folder}.", folder);
            }
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace('\\', '/');

    private static string StemOf(string path) => Path.ChangeExtension(NormalizePath(path), null);

    private static UnusedFilesReportVM Report(List<UnusedGroup> groups, bool removed)
    {
        var vms = groups.Select(g => new UnusedFileGroupVM
        {
            Kind = g.Kind,
            Folder = g.Folder,
            Files = g.Entries.Sum(e => e.Files),
            Bytes = g.Entries.Sum(e => e.Bytes),
            Removable = g.Removable,
            Examples = g.Entries.Take(ExampleCount).Select(e => Path.GetFileName(e.Path)).ToList()
        }).ToList();

        return new UnusedFilesReportVM
        {
            ScannedAt = DateTime.UtcNow,
            Removed = removed,
            RemovableFiles = vms.Where(g => g.Removable).Sum(g => g.Files),
            RemovableBytes = vms.Where(g => g.Removable).Sum(g => g.Bytes),
            Groups = vms
        };
    }

    [GeneratedRegex(@"_\d{8}_\d{6}\.(ts|mp4)$", RegexOptions.IgnoreCase)]
    private static partial Regex RecordingName();

    [GeneratedRegex(@"^(media_|coll_|playlist_|music_|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}_overlay_)", RegexOptions.IgnoreCase)]
    private static partial Regex ArtworkFileName();

    [GeneratedRegex(@"^profile_", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileFileName();

    [GeneratedRegex(@".")]
    private static partial Regex AnyFileName();

    private sealed record UnusedEntry(string Path, bool IsFolder, int Files, long Bytes);

    private sealed record UnusedGroup(UnusedFileKind Kind, string Folder, bool Removable)
    {
        public List<UnusedEntry> Entries { get; } = new();
    }
}
