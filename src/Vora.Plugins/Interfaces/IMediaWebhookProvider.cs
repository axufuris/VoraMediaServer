using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IMediaWebhookProvider : IVoraPlugin
{
    string DefaultConnectionName { get; }

    string SetupInstructions { get; }

    string? RequestServerProviderId => null;

    IEnumerable<MediaWebhookSettingDefinition> GetConnectionSettingDefinitions() => Array.Empty<MediaWebhookSettingDefinition>();

    LibraryKind? ResolveLibraryKind(IReadOnlyDictionary<string, string> settings);

    MediaWebhookParseResult Parse(string body, IReadOnlyDictionary<string, string> settings);
}
