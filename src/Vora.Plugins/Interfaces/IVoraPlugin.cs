using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IVoraPlugin
{
    string Id { get; }
    string Name { get; }
    string ProviderName => Name;
    string Version { get; }
    string Description { get; }
    bool IsSystemPlugin { get; }
    string Type { get; }
    string? DeveloperName => null;
    string? LatestVersionApiUrl => null;
    string? DocumentationUrl => null;
    bool IsAiPlugin => false;
    int ContractVersion => 1;

    IEnumerable<LibraryKind> SupportedLibraryKinds => new[]
    {
        LibraryKind.Movie,
        LibraryKind.TvShow,
        LibraryKind.Music,
        LibraryKind.HomeVideo,
    };

    IEnumerable<string> SupportedLibraryTypes =>
        SupportedLibraryKinds.Select(k => k.ToString());

    IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions();
}
