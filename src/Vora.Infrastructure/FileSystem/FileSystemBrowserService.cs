using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vora.Application.FileSystem;
using Vora.Application.FileSystem.ViewModels;

namespace Vora.Infrastructure.FileSystem;

public class FileSystemBrowserService : IFileSystemBrowserService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileSystemBrowserService> _logger;

    public FileSystemBrowserService(IConfiguration configuration, ILogger<FileSystemBrowserService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<List<FileSystemRootVM>> GetAllowedRootsAsync()
    {
        var roots = ResolveAllowedRoots();
        return Task.FromResult(roots);
    }

    public Task<FileSystemListingVM> ListAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var normalized = NormalizePath(path);
        var allowedRoots = ResolveAllowedRoots();

        if (!IsInsideAllowedRoot(normalized, allowedRoots, out var matchedRoot))
        {
            throw new UnauthorizedAccessException("Path is not inside any allowed media root.");
        }

        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException($"Directory not found: {normalized}");
        }

        var folders = new List<FileSystemEntryVM>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(normalized))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith('.'))
                {
                    continue;
                }

                bool hasChildren = false;
                try
                {
                    hasChildren = Directory.EnumerateDirectories(dir).Any(d =>
                    {
                        var n = Path.GetFileName(d);
                        return !string.IsNullOrEmpty(n) && !n.StartsWith('.');
                    });
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }

                folders.Add(new FileSystemEntryVM
                {
                    Name = name,
                    Path = NormalizePath(dir),
                    HasChildren = hasChildren
                });
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied listing folder {Path}", normalized);
            throw;
        }

        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        string? parentPath = null;
        if (!IsRootPath(normalized, matchedRoot))
        {
            var parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                var normalizedParent = NormalizePath(parent);
                if (IsInsideAllowedRoot(normalizedParent, allowedRoots, out _))
                {
                    parentPath = normalizedParent;
                }
            }
        }

        return Task.FromResult(new FileSystemListingVM
        {
            Path = normalized,
            ParentPath = parentPath,
            Folders = folders
        });
    }

    private static readonly string[] ExcludedMountPrefixes =
    {
        "/proc", "/sys", "/dev", "/run", "/etc", "/app", "/transcode",
    };

    private List<FileSystemRootVM> ResolveAllowedRoots()
    {
        var configured = _configuration.GetSection("FileSystemBrowser:AllowedRoots").Get<List<FileSystemRootVM>>();

        if (configured != null && configured.Count > 0)
        {
            return ResolveConfiguredRoots(configured);
        }

        var discovered = DiscoverMountRoots();
        if (discovered.Count == 0)
        {
            _logger.LogWarning("No browsable media mounts were auto-detected and FileSystemBrowser:AllowedRoots is not set. Mount your media into the container (or set the config) to enable browsing.");
        }
        return discovered;
    }

    private List<FileSystemRootVM> ResolveConfiguredRoots(List<FileSystemRootVM> configured)
    {
        var resolved = new List<FileSystemRootVM>();
        foreach (var root in configured)
        {
            if (string.IsNullOrWhiteSpace(root.Path))
            {
                continue;
            }

            var normalized = NormalizePath(root.Path);
            if (IsDriveRoot(normalized))
            {
                _logger.LogWarning("Refusing to allow drive root {Path} as a filesystem browser root. Configure a specific media folder instead.", normalized);
                continue;
            }

            if (!Directory.Exists(normalized))
            {
                _logger.LogDebug("Allowed root {Path} does not exist; skipping", normalized);
                continue;
            }

            resolved.Add(new FileSystemRootVM
            {
                Label = string.IsNullOrWhiteSpace(root.Label) ? DeriveRootLabel(normalized) : root.Label,
                Path = normalized
            });
        }

        return resolved;
    }

    private List<FileSystemRootVM> DiscoverMountRoots()
    {
        const string mountInfoPath = "/proc/self/mountinfo";
        var results = new List<FileSystemRootVM>();

        if (!File.Exists(mountInfoPath))
        {
            return results;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(mountInfoPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read {Path} for media mount auto-detection.", mountInfoPath);
            return results;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var fields = line.Split(' ');
            if (fields.Length < 5)
            {
                continue;
            }

            var mountPoint = UnescapeMountPath(fields[4]);
            if (string.IsNullOrWhiteSpace(mountPoint))
            {
                continue;
            }

            var normalized = NormalizePath(mountPoint);
            if (!seen.Add(normalized)) continue;
            if (IsDriveRoot(normalized)) continue;
            if (IsExcludedMount(normalized)) continue;
            if (!Directory.Exists(normalized)) continue;

            results.Add(new FileSystemRootVM
            {
                Label = DeriveRootLabel(normalized),
                Path = normalized
            });
        }

        results.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private static bool IsExcludedMount(string normalizedPath)
    {
        foreach (var prefix in ExcludedMountPrefixes)
        {
            if (string.Equals(normalizedPath, prefix, StringComparison.Ordinal) ||
                normalizedPath.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string UnescapeMountPath(string raw)
    {
        if (!raw.Contains('\\'))
        {
            return raw;
        }

        return raw
            .Replace("\\040", " ")
            .Replace("\\011", "\t")
            .Replace("\\012", "\n")
            .Replace("\\134", "\\");
    }

    private static bool IsDriveRoot(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath)) return true;
        if (normalizedPath == "/") return true;
        if (normalizedPath.Length == 3 && normalizedPath[1] == ':' && normalizedPath[2] == Path.DirectorySeparatorChar) return true;
        if (normalizedPath.Length == 2 && normalizedPath[1] == ':') return true;
        return false;
    }

    private static string DeriveRootLabel(string normalizedPath)
    {
        if (normalizedPath == "/" || normalizedPath.Length == 0)
        {
            return "Filesystem";
        }
        var trimmed = normalizedPath.TrimEnd(Path.DirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim();
        var full = Path.GetFullPath(trimmed);
        if (full.Length > 1 && full.EndsWith(Path.DirectorySeparatorChar))
        {
            if (full.Length == 3 && full[1] == ':')
            {
                return full;
            }
            full = full.TrimEnd(Path.DirectorySeparatorChar);
        }
        return full;
    }

    private static bool IsInsideAllowedRoot(string path, List<FileSystemRootVM> roots, out FileSystemRootVM? matched)
    {
        foreach (var root in roots)
        {
            if (IsPathAtOrUnder(path, root.Path))
            {
                matched = root;
                return true;
            }
        }
        matched = null;
        return false;
    }

    private static bool IsPathAtOrUnder(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool IsRootPath(string path, FileSystemRootVM? matchedRoot)
    {
        if (matchedRoot == null)
        {
            return false;
        }
        return string.Equals(path, matchedRoot.Path, StringComparison.Ordinal);
    }
}
