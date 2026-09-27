using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Templates;

namespace Vora.Application.Templates;

public interface IClientTemplateManager
{
    Task<IReadOnlyList<TemplateMetaVM>> GetAllAsync();
    Task<ActiveTemplateVM> GetActiveAsync(Guid profileId);
    Task<SetActiveTemplateResponse> SetActiveAsync(Guid profileId, string templateId);
    Task<bool> ClearActiveAsync(Guid profileId);
    Task<string> GetDefaultAsync();
    Task<bool> SetDefaultAsync(string templateId);
    int RescanBundles();
}

public class ClientTemplateManager : IClientTemplateManager
{
    private const string FallbackTemplateId = "vora-cinema";

    private readonly IClientTemplateRegistry _registry;
    private readonly IClientTemplateBundleLoader _bundleLoader;
    private readonly IClientTemplateScheduleManager _scheduleManager;
    private readonly ISystemSettingsRepository _settingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClientNotifier _notifier;

    public ClientTemplateManager(
        IClientTemplateRegistry registry,
        IClientTemplateBundleLoader bundleLoader,
        IClientTemplateScheduleManager scheduleManager,
        ISystemSettingsRepository settingsRepository,
        IUserRepository userRepository,
        IClientNotifier notifier)
    {
        _registry = registry;
        _bundleLoader = bundleLoader;
        _scheduleManager = scheduleManager;
        _settingsRepository = settingsRepository;
        _userRepository = userRepository;
        _notifier = notifier;
    }

    public Task<IReadOnlyList<TemplateMetaVM>> GetAllAsync()
        => Task.FromResult(_registry.GetAll());

    public async Task<ActiveTemplateVM> GetActiveAsync(Guid profileId)
    {
        var profile = await _userRepository.GetProfileByIdAsync(profileId);
        var defaultId = await ResolveDefaultIdAsync();

        var schedule = await _scheduleManager.GetActiveScheduleAsync(DateTime.UtcNow);
        if (schedule != null)
        {
            var scheduleVM = ToScheduleVM(schedule);

            if (profile != null
                && profile.ScheduleOverrideScheduleId == schedule.Id
                && !string.IsNullOrWhiteSpace(profile.ScheduleOverrideTemplateId)
                && _registry.Exists(profile.ScheduleOverrideTemplateId!))
            {
                return new ActiveTemplateVM
                {
                    TemplateId = profile.ScheduleOverrideTemplateId!,
                    Source = ActiveTemplateSource.Override,
                    Schedule = scheduleVM,
                };
            }

            return new ActiveTemplateVM
            {
                TemplateId = schedule.TemplateId,
                Source = ActiveTemplateSource.Schedule,
                Schedule = scheduleVM,
            };
        }

        if (profile != null
            && !string.IsNullOrWhiteSpace(profile.ClientTemplateId)
            && _registry.Exists(profile.ClientTemplateId!))
        {
            return new ActiveTemplateVM
            {
                TemplateId = profile.ClientTemplateId!,
                Source = ActiveTemplateSource.Profile,
            };
        }

        return new ActiveTemplateVM
        {
            TemplateId = defaultId,
            Source = ActiveTemplateSource.Default,
        };
    }

    public async Task<SetActiveTemplateResponse> SetActiveAsync(Guid profileId, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentException("TemplateId is required.");
        if (!_registry.Exists(templateId)) throw new ArgumentException($"Unknown templateId: {templateId}");

        var profile = await _userRepository.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException("Profile not found.");

        var schedule = await _scheduleManager.GetActiveScheduleAsync(DateTime.UtcNow);

        ActiveTemplateSource source;
        if (schedule != null)
        {
            if (string.Equals(templateId, schedule.TemplateId, StringComparison.Ordinal))
            {
                profile.ScheduleOverrideTemplateId = null;
                profile.ScheduleOverrideScheduleId = null;
                source = ActiveTemplateSource.Schedule;
            }
            else
            {
                profile.ScheduleOverrideTemplateId = templateId;
                profile.ScheduleOverrideScheduleId = schedule.Id;
                source = ActiveTemplateSource.Override;
            }
        }
        else
        {
            profile.ClientTemplateId = templateId;
            profile.ScheduleOverrideTemplateId = null;
            profile.ScheduleOverrideScheduleId = null;
            source = ActiveTemplateSource.Profile;
        }

        await _userRepository.UpdateProfileAsync(profile);
        await _notifier.NotifyClientTemplateChangedForProfileAsync(profileId);
        return new SetActiveTemplateResponse(templateId, source);
    }

    public async Task<bool> ClearActiveAsync(Guid profileId)
    {
        var profile = await _userRepository.GetProfileByIdAsync(profileId);
        if (profile == null) return false;

        profile.ClientTemplateId = null;
        profile.ScheduleOverrideTemplateId = null;
        profile.ScheduleOverrideScheduleId = null;
        await _userRepository.UpdateProfileAsync(profile);
        await _notifier.NotifyClientTemplateChangedForProfileAsync(profileId);
        return true;
    }

    public async Task<string> GetDefaultAsync()
    {
        return await ResolveDefaultIdAsync();
    }

    public async Task<bool> SetDefaultAsync(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return false;
        if (!_registry.Exists(templateId)) return false;

        var settings = await _settingsRepository.GetSettingsForUpdateAsync();
        settings.DefaultClientTemplateId = templateId;
        await _settingsRepository.SaveChangesAsync();

        await _notifier.NotifyClientTemplateConfigurationChangedAsync();
        return true;
    }

    public int RescanBundles() => _bundleLoader.Refresh();

    private async Task<string> ResolveDefaultIdAsync()
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        var id = settings.DefaultClientTemplateId;
        return _registry.Exists(id) ? id : FallbackTemplateId;
    }

    private TemplateScheduleVM ToScheduleVM(ClientTemplateSchedule entity) => new()
    {
        Id = entity.Id,
        TemplateId = entity.TemplateId,
        Name = entity.Name,
        StartsAtUtc = entity.StartsAtUtc,
        EndsAtUtc = entity.EndsAtUtc,
        Priority = entity.Priority,
        Enabled = entity.Enabled,
        TemplateMissing = !_registry.Exists(entity.TemplateId),
    };
}
