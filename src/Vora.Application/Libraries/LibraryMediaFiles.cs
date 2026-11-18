using Vora.Domain.Enums;
using Vora.Plugins;

namespace Vora.Application.Libraries;

public static class LibraryMediaFiles
{
    public static IReadOnlyList<string> ExtensionsFor(LibraryType libraryType) => libraryType switch
    {
        LibraryType.Music => MediaFileExtensions.Audio,
        LibraryType.HomeVideo => MediaFileExtensions.HomeVideo,
        _ => MediaFileExtensions.Video
    };

    public static bool IsSupportedFor(string filePath, LibraryType libraryType) =>
        ExtensionsFor(libraryType).Contains(Path.GetExtension(filePath).ToLowerInvariant());
}
