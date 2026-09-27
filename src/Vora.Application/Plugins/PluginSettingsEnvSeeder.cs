using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Plugins;

public interface IPluginSettingsEnvSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public sealed class PluginSettingsEnvSeeder : IPluginSettingsEnvSeeder
{
    private const string SectionName = "Vora:PluginSettings";
    private const string EnabledSettingKey = "is_enabled";

    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IVoraPlugin> _plugins;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly ILogger<PluginSettingsEnvSeeder> _logger;

    public PluginSettingsEnvSeeder(
        IConfiguration configuration,
        IEnumerable<IVoraPlugin> plugins,
        ISystemSettingsRepository settingsRepo,
        ILogger<PluginSettingsEnvSeeder> logger)
    {
        _configuration = configuration;
        _plugins = plugins;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var section = _configuration.GetSection(SectionName);
        if (!section.Exists())
        {
            return;
        }

        var pluginsById = _plugins.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var seeded = new List<string>();
        var skippedExisting = new List<string>();

        foreach (var pluginSection in section.GetChildren())
        {
            ct.ThrowIfCancellationRequested();

            var pluginId = pluginSection.Key;
            if (!pluginsById.TryGetValue(pluginId, out var plugin))
            {
                _logger.LogWarning(
                    "Plugin settings environment variable references plugin '{PluginId}', but no plugin with that id is installed — skipping.",
                    pluginId);
                continue;
            }

            var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EnabledSettingKey };
            foreach (var def in plugin.GetSettingDefinitions())
            {
                validKeys.Add(def.Key);
            }

            foreach (var leaf in pluginSection.GetChildren())
            {
                ct.ThrowIfCancellationRequested();

                var settingKey = leaf.Key;
                var value = leaf.Value;

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!validKeys.Contains(settingKey))
                {
                    _logger.LogWarning(
                        "Plugin '{PluginId}' has no setting named '{SettingKey}' — skipping seed value.",
                        pluginId, settingKey);
                    continue;
                }

                var existing = await _settingsRepo.GetPluginSettingAsync(pluginId, settingKey);
                if (!string.IsNullOrEmpty(existing))
                {
                    skippedExisting.Add($"{pluginId}.{settingKey}");
                    continue;
                }

                await _settingsRepo.SetPluginSettingAsync(pluginId, settingKey, value);
                seeded.Add($"{pluginId}.{settingKey}");
            }
        }

        if (seeded.Count > 0)
        {
            _logger.LogInformation(
                "Seeded {Count} plugin setting(s) from environment: {Keys}",
                seeded.Count, string.Join(", ", seeded));
        }

        if (skippedExisting.Count > 0)
        {
            _logger.LogInformation(
                "Skipped {Count} plugin setting(s) already present in the database: {Keys}",
                skippedExisting.Count, string.Join(", ", skippedExisting));
        }
    }
}
