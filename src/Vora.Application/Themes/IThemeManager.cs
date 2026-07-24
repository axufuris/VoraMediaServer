using Vora.Application.Analysis;
using Vora.Application.Settings;

namespace Vora.Application.Themes;

public interface IThemeManager
{
    Task<IReadOnlyList<ThemeMetaVM>> GetAllAsync();
    Task<string> GetActiveIdAsync();
    Task<bool> SetActiveIdAsync(string themeId);
    int RescanBundles();
}

public class ThemeManager : IThemeManager
{
    private const string FallbackThemeId = "vora-dark";

    private readonly IThemeRegistry _registry;
    private readonly IThemeBundleLoader _bundleLoader;
    private readonly ISystemSettingsRepository _settingsRepository;
    private readonly IClientNotifier _notifier;

    public ThemeManager(
        IThemeRegistry registry,
        IThemeBundleLoader bundleLoader,
        ISystemSettingsRepository settingsRepository,
        IClientNotifier notifier)
    {
        _registry = registry;
        _bundleLoader = bundleLoader;
        _settingsRepository = settingsRepository;
        _notifier = notifier;
    }

    public Task<IReadOnlyList<ThemeMetaVM>> GetAllAsync()
        => Task.FromResult(_registry.GetAll());

    public async Task<string> GetActiveIdAsync()
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        var id = settings.AdminThemeId;
        return _registry.Exists(id) ? id : FallbackThemeId;
    }

    public async Task<bool> SetActiveIdAsync(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return false;
        if (!_registry.Exists(themeId)) return false;

        var settings = await _settingsRepository.GetSettingsForUpdateAsync();
        settings.AdminThemeId = themeId;
        await _settingsRepository.SaveChangesAsync();

        // Tell everyone connected to re-apply the new theme without a page
        // reload. Sent to all clients (not just admins) because ThemeProvider
        // mounts at the app root and the CSS variables update everywhere.
        await _notifier.NotifyAdminThemeChangedAsync(themeId);
        return true;
    }

    public int RescanBundles() => _bundleLoader.Refresh();
}
