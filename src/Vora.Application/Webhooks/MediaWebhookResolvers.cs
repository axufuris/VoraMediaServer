using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Domain.Enums;
using Vora.Plugins;
using Vora.Plugins.Dtos;

namespace Vora.Application.Webhooks;

public static class MediaWebhookResolvers
{
    public static async Task<MediaWebhookPathResolver> CreateAsync(
        ILibraryRepository libraryRepo,
        ISystemSettingsRepository settingsRepo,
        IMediaRepository mediaRepo,
        LibraryType type,
        string? pathMappings)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        var libraries = (await libraryRepo.GetAllProjectedAsync(l => new MediaWebhookLibrary(l.Id, l.Name, l.Type, l.FolderPaths, l.ExcludeFilters)))
            .Where(l => l.Type == type)
            .Select(l => l with { ExcludeFilters = LibraryFileFilter.Combine(l.ExcludeFilters, settings.ScanIgnoredFolders) })
            .ToList();

        return new MediaWebhookPathResolver(
            MediaWebhookPathMappings.Parse(pathMappings),
            libraries,
            async top => Directory.Exists(top) || File.Exists(top) || await mediaRepo.AnyMediaUnderPathAsync(top));
    }

    public static LibraryType? ToLibraryType(LibraryKind? kind) => kind switch
    {
        LibraryKind.Movie => LibraryType.Movie,
        LibraryKind.TvShow => LibraryType.TvShow,
        LibraryKind.Music => LibraryType.Music,
        LibraryKind.HomeVideo => LibraryType.HomeVideo,
        _ => null
    };
}
