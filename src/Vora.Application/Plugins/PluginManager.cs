using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.FileSystem;
using Vora.Application.Plugins.ViewModels;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Plugins;

public interface IPluginManager
{
    Task<IEnumerable<PluginVM>> GetActivePluginsAsync();
    Task<IEnumerable<PluginOptionVM>> GetPluginOptionsAsync(string type);
    Task<PluginConnectionTestResult> TestPluginConnectionAsync(string pluginId, IReadOnlyDictionary<string, string> settings);
    Task UploadPluginAsync(UploadedFile file);
    bool UninstallPlugin(string id);
}

public class PluginManager(
    IEnumerable<IVoraPlugin> plugins,
    ISystemSettingsRepository settingsRepo,
    IOptions<StoragePathsOptions> storagePaths,
    Vora.Application.Ai.IOpenAiClient openAi,
    ILogger<PluginManager> logger) : IPluginManager
{
    private const string EnabledSettingKey = "is_enabled";
    private const string DisabledValue = "false";

    public async Task<IEnumerable<PluginVM>> GetActivePluginsAsync()
    {
        var result = new List<PluginVM>();

        foreach (var plugin in plugins)
        {
            var isEnabledStr = await settingsRepo.GetPluginSettingAsync(plugin.Id, EnabledSettingKey);
            var definitions = plugin.GetSettingDefinitions().ToList();

            var requiresConfiguration = false;
            if (definitions.Count > 0)
            {
                var saved = await settingsRepo.GetAllPluginSettingsAsync(plugin.Id);
                requiresConfiguration = RequiresConfiguration(definitions, saved);
            }

            result.Add(new PluginVM
            {
                Id = plugin.Id,
                Name = plugin.Name,
                Version = plugin.Version,
                Description = plugin.Description,
                IsSystemPlugin = plugin.IsSystemPlugin,
                Type = plugin.Type,
                DeveloperName = plugin.DeveloperName,
                LatestVersionApiUrl = plugin.LatestVersionApiUrl,
                DocumentationUrl = plugin.DocumentationUrl,
                ExternalConfigurationHint = plugin.ExternalConfigurationHint,
                HasSettings = definitions.Count > 0,
                IsAiPlugin = plugin.IsAiPlugin,
                IsEnabled = isEnabledStr != DisabledValue,
                RequiresConfiguration = requiresConfiguration,
                SupportsConnectionTest = plugin is IPluginConnectionTest
            });
        }

        return result;
    }

    public async Task<PluginConnectionTestResult> TestPluginConnectionAsync(string pluginId, IReadOnlyDictionary<string, string> settings)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null)
        {
            return PluginConnectionTestResult.Fail("Plugin not found.");
        }

        if (plugin is not IPluginConnectionTest testable)
        {
            return PluginConnectionTestResult.Fail("This plugin does not support connection testing.");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await testable.TestConnectionAsync(settings, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return PluginConnectionTestResult.Fail("The connection test timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection test failed for plugin {PluginId}.", pluginId);
            return PluginConnectionTestResult.Fail($"Connection test failed: {ex.Message}");
        }
    }

    public async Task<IEnumerable<PluginOptionVM>> GetPluginOptionsAsync(string type)
    {
        var targetPlugins = plugins
            .Where(p => p.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Id)
            .ToList();

        var validOptions = new List<PluginOptionVM>();
        var aiConfigured = targetPlugins.Any(p => p.IsAiPlugin) && await openAi.IsConfiguredAsync();

        foreach (var plugin in targetPlugins)
        {
            var isEnabledStr = await settingsRepo.GetPluginSettingAsync(plugin.Id, EnabledSettingKey);
            if (isEnabledStr == DisabledValue)
            {
                continue;
            }

            if (plugin.IsAiPlugin && !aiConfigured)
            {
                continue;
            }

            if (!await HasAllRequiredSettingsAsync(plugin))
            {
                continue;
            }

            validOptions.Add(new PluginOptionVM
            {
                Id = plugin.Id,
                Name = plugin.Name,
                ExternalIdLabel = ResolveExternalIdLabel(plugin),
                ExternalIdPlaceholder = ResolveExternalIdPlaceholder(plugin),
                IsAiPlugin = plugin.IsAiPlugin,
                SupportedLibraryTypes = plugin.SupportedLibraryTypes
            });
        }

        return validOptions;
    }

    public async Task UploadPluginAsync(UploadedFile file)
    {
        if (file == null || !file.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .dll plugin files are supported.");
        }

        var configured = storagePaths.Value.Plugins;
        var pluginsPath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsPath))
        {
            Directory.CreateDirectory(pluginsPath);
        }

        var filePath = Path.Combine(pluginsPath, file.FileName);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.Content.CopyToAsync(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload plugin {FileName} to {FilePath}.", file.FileName, filePath);
            throw;
        }
    }

    public bool UninstallPlugin(string id)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == id);
        if (plugin == null)
        {
            return false;
        }

        if (plugin.IsSystemPlugin)
        {
            throw new InvalidOperationException("Cannot delete system plugins.");
        }

        var assemblyPath = plugin.GetType().Assembly.Location;
        try
        {
            if (File.Exists(assemblyPath))
            {
                File.Move(assemblyPath, assemblyPath + ".deleted", overwrite: true);
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark plugin {PluginId} for deletion at {AssemblyPath}.", id, assemblyPath);
            throw;
        }
    }

    private static bool RequiresConfiguration(
        IReadOnlyList<Vora.Plugins.Dtos.PluginSettingDefinitionDto> definitions,
        IReadOnlyDictionary<string, string> savedSettings)
    {
        return definitions.Any(def =>
            string.IsNullOrEmpty(def.DefaultValue) &&
            (!savedSettings.TryGetValue(def.Key, out var value) || string.IsNullOrWhiteSpace(value)));
    }

    private async Task<bool> HasAllRequiredSettingsAsync(IVoraPlugin plugin)
    {
        var definitions = plugin.GetSettingDefinitions().ToList();
        if (definitions.Count == 0)
        {
            return true;
        }

        var savedSettings = await settingsRepo.GetAllPluginSettingsAsync(plugin.Id);
        return definitions.All(def => savedSettings.TryGetValue(def.Key, out var val) && !string.IsNullOrWhiteSpace(val));
    }

    private static string ResolveExternalIdLabel(IVoraPlugin plugin) => plugin switch
    {
        IChronologyProvider cp => cp.ExternalIdLabel,
        ICollectionSyncProvider csp => csp.ExternalIdLabel,
        _ => "ID"
    };

    private static string ResolveExternalIdPlaceholder(IVoraPlugin plugin) => plugin switch
    {
        IChronologyProvider cp => cp.ExternalIdPlaceholder,
        ICollectionSyncProvider csp => csp.ExternalIdPlaceholder,
        _ => "Enter ID"
    };
}
