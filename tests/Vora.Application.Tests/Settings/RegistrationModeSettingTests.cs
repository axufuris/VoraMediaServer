using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Settings;

public class RegistrationModeSettingTests
{
    private readonly ISystemSettingsRepository _repo = Substitute.For<ISystemSettingsRepository>();
    private readonly ServerSetting _settings = new() { RegistrationMode = RegistrationMode.Invitation };
    private readonly SystemSettingsManager _manager;

    public RegistrationModeSettingTests()
    {
        _repo.GetSettingsForUpdateAsync().Returns(_settings);
        _manager = new SystemSettingsManager(_repo, Array.Empty<IVoraPlugin>(), Substitute.For<IServiceProvider>());
    }

    [Fact]
    public async Task Saving_system_settings_leaves_the_registration_mode_alone()
    {
        var stale = ServerSettingsVM.Projection.Compile()(new ServerSetting { RegistrationMode = RegistrationMode.Simple });

        await _manager.UpdateServerSettingsAsync(stale);

        _settings.RegistrationMode.Should().Be(RegistrationMode.Invitation);
    }

    [Fact]
    public async Task The_registration_mode_is_saved_on_its_own()
    {
        await _manager.UpdateRegistrationModeAsync(RegistrationMode.Disabled);

        _settings.RegistrationMode.Should().Be(RegistrationMode.Disabled);
        await _repo.Received(1).SaveChangesAsync();
    }
}
