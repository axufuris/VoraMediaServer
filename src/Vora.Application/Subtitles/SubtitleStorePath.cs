using Vora.Application.Settings;

namespace Vora.Application.Subtitles;

// Where downloaded subtitles live. This path was added after most servers were
// already deployed, so it cannot assume an admin has set it: falling back to
// AppContext.BaseDirectory puts it at /app/subtitles inside the container, which
// is part of the read-only image and not one of the writable mounts.
//
// So an unset value is resolved beside a path the deployment already configures
// and already persists — every compose file mounts one of these under the data
// volume. That makes the feature work on an existing server with no compose
// change, which is the only way an added path is ever safe.
public static class SubtitleStorePath
{
    public const string DirectoryName = "subtitles";

    // Everything downloaded for one item shares a directory, which is what makes
    // deleting the item a single directory removal.
    public static string ItemDirectory(StoragePathsOptions paths, Guid mediaItemId) =>
        Path.Combine(Resolve(paths), mediaItemId.ToString("N")[..2], mediaItemId.ToString("N"));

    public static string Resolve(StoragePathsOptions paths)
    {
        if (!string.IsNullOrWhiteSpace(paths.Subtitles)) return paths.Subtitles!;

        var dataRoot = FirstConfiguredParent(paths.VideoThumbnails, paths.CustomArtwork, paths.Backups, paths.Logs, paths.Metadata);

        return Path.Combine(dataRoot ?? AppContext.BaseDirectory, DirectoryName);
    }

    private static string? FirstConfiguredParent(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var parent = Path.GetDirectoryName(candidate.TrimEnd('/', '\\'));
            if (!string.IsNullOrWhiteSpace(parent)) return parent;
        }

        return null;
    }
}
