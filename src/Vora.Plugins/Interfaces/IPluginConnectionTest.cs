using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IPluginConnectionTest
{
    Task<PluginConnectionTestResult> TestConnectionAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
}
