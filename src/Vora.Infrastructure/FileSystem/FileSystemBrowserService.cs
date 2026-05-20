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

    private List<FileSystemRootVM> ResolveAllowedRoots()
    {
        var configured = _configuration.GetSection("FileSystemBrowser:AllowedRoots").Get<List<FileSystemRootVM>>();

        if (configured == null || configured.Count == 0)
        {
            configured = BuildDefaultRoots();
        }

        var resolved = new List<FileSystemRootVM>();
        foreach (var root in configured)
        {
            if (string.IsNullOrWhiteSpace(root.Path))
            {
                continue;
            }

            var normalized = NormalizePath(root.Path);
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

    private static List<FileSystemRootVM> BuildDefaultRoots()
    {
        var defaults = new List<FileSystemRootVM>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                defaults.Add(new FileSystemRootVM
                {
                    Label = drive.Name.TrimEnd(Path.DirectorySeparatorChar),
                    Path = drive.RootDirectory.FullName
                });
            }
        }
        else
        {
            defaults.Add(new FileSystemRootVM { Label = "Filesystem", Path = "/" });
        }

        return defaults;
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
