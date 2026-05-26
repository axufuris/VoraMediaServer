using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Templates;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Templates;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Templates;

public class ClientTemplateManagerTests
{
    private readonly IClientTemplateRegistry _registry;
    private readonly IClientTemplateBundleLoader _bundles;
    private readonly IClientTemplateScheduleManager _schedules;
    private readonly ISystemSettingsRepository _settings;
    private readonly IUserRepository _users;
    private readonly IClientNotifier _notifier;
    private readonly ClientTemplateManager _manager;

    public ClientTemplateManagerTests()
    {
        _registry = Substitute.For<IClientTemplateRegistry>();
        _bundles = Substitute.For<IClientTemplateBundleLoader>();
        _schedules = Substitute.For<IClientTemplateScheduleManager>();
        _settings = Substitute.For<ISystemSettingsRepository>();
        _users = Substitute.For<IUserRepository>();
        _notifier = Substitute.For<IClientNotifier>();

        // Common defaults: vora-cinema is the default fallback id
        _registry.Exists("vora-cinema").Returns(true);
        _settings.GetSettingsAsync().Returns(new ServerSetting { DefaultClientTemplateId = "vora-cinema" });

        _manager = new ClientTemplateManager(_registry, _bundles, _schedules, _settings, _users, _notifier);
    }

    private static UserProfile MakeProfile(
        Guid id,
        string? clientTemplateId = null,
        string? scheduleOverrideTemplateId = null,
        Guid? scheduleOverrideScheduleId = null)
    {
        return new UserProfile
        {
            Id = id,
            Name = "p",
            UserId = Guid.NewGuid(),
            ClientTemplateId = clientTemplateId,
            ScheduleOverrideTemplateId = scheduleOverrideTemplateId,
            ScheduleOverrideScheduleId = scheduleOverrideScheduleId
        };
    }

    private static ClientTemplateSchedule MakeSchedule(string templateId, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TemplateId = templateId,
        Name = "Holiday window",
        StartsAtUtc = DateTime.UtcNow.AddDays(-1),
        EndsAtUtc = DateTime.UtcNow.AddDays(1),
        Priority = 0,
        Enabled = true
    };

    // ---------- GetActiveAsync resolution chain ----------

    [Fact]
    public async Task GetActiveAsync_returns_server_default_when_no_schedule_and_no_profile_template()
    {
        var profileId = Guid.NewGuid();
        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(profileId));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-cinema");
        result.Source.Should().Be(ActiveTemplateSource.Default);
        result.Schedule.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_falls_back_to_vora_cinema_when_settings_default_is_unknown_template()
    {
        var profileId = Guid.NewGuid();
        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(profileId));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);
        _settings.GetSettingsAsync().Returns(new ServerSetting { DefaultClientTemplateId = "unknown-template" });
        _registry.Exists("unknown-template").Returns(false);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-cinema");
        result.Source.Should().Be(ActiveTemplateSource.Default);
    }

    [Fact]
    public async Task GetActiveAsync_returns_profile_template_when_set_and_no_schedule()
    {
        var profileId = Guid.NewGuid();
        _registry.Exists("vora-noir").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(profileId, clientTemplateId: "vora-noir"));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-noir");
        result.Source.Should().Be(ActiveTemplateSource.Profile);
    }

    [Fact]
    public async Task GetActiveAsync_falls_back_to_default_when_profile_template_is_unknown()
    {
        var profileId = Guid.NewGuid();
        _registry.Exists("missing-id").Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(profileId, clientTemplateId: "missing-id"));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-cinema");
        result.Source.Should().Be(ActiveTemplateSource.Default);
    }

    [Fact]
    public async Task GetActiveAsync_returns_schedule_template_when_schedule_active_and_no_override()
    {
        var profileId = Guid.NewGuid();
        var schedule = MakeSchedule("vora-velvet");
        _registry.Exists("vora-velvet").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(profileId, clientTemplateId: "vora-noir"));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(schedule);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-velvet");
        result.Source.Should().Be(ActiveTemplateSource.Schedule);
        result.Schedule.Should().NotBeNull();
        result.Schedule!.Id.Should().Be(schedule.Id);
    }

    [Fact]
    public async Task GetActiveAsync_returns_override_when_profile_overrides_the_active_schedule()
    {
        var profileId = Guid.NewGuid();
        var schedule = MakeSchedule("vora-velvet");
        _registry.Exists("vora-velvet").Returns(true);
        _registry.Exists("vora-aurora").Returns(true);

        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(
            profileId,
            scheduleOverrideTemplateId: "vora-aurora",
            scheduleOverrideScheduleId: schedule.Id));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(schedule);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-aurora");
        result.Source.Should().Be(ActiveTemplateSource.Override);
        result.Schedule!.Id.Should().Be(schedule.Id);
    }

    [Fact]
    public async Task GetActiveAsync_ignores_override_when_pointing_at_a_different_schedule()
    {
        // Override is stale (points to a previous schedule id).
        var profileId = Guid.NewGuid();
        var currentSchedule = MakeSchedule("vora-velvet");
        var oldScheduleId = Guid.NewGuid();

        _registry.Exists("vora-velvet").Returns(true);
        _registry.Exists("vora-aurora").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(
            profileId,
            scheduleOverrideTemplateId: "vora-aurora",
            scheduleOverrideScheduleId: oldScheduleId));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(currentSchedule);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-velvet");
        result.Source.Should().Be(ActiveTemplateSource.Schedule);
    }

    [Fact]
    public async Task GetActiveAsync_ignores_override_when_override_template_is_unknown()
    {
        var profileId = Guid.NewGuid();
        var schedule = MakeSchedule("vora-velvet");
        _registry.Exists("vora-velvet").Returns(true);
        _registry.Exists("missing-template").Returns(false);

        _users.GetProfileByIdAsync(profileId).Returns(MakeProfile(
            profileId,
            scheduleOverrideTemplateId: "missing-template",
            scheduleOverrideScheduleId: schedule.Id));
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(schedule);

        var result = await _manager.GetActiveAsync(profileId);

        result.TemplateId.Should().Be("vora-velvet");
        result.Source.Should().Be(ActiveTemplateSource.Schedule);
    }

    [Fact]
    public async Task GetActiveAsync_returns_default_when_profile_is_null_and_no_schedule()
    {
        var profileId = Guid.NewGuid();
        _users.GetProfileByIdAsync(profileId).Returns((UserProfile?)null);
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);

        var result = await _manager.GetActiveAsync(profileId);

        result.Source.Should().Be(ActiveTemplateSource.Default);
        result.TemplateId.Should().Be("vora-cinema");
    }

    [Fact]
    public async Task GetActiveAsync_returns_schedule_when_profile_is_null_and_schedule_active()
    {
        var profileId = Guid.NewGuid();
        var schedule = MakeSchedule("vora-velvet");
        _registry.Exists("vora-velvet").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns((UserProfile?)null);
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(schedule);

        var result = await _manager.GetActiveAsync(profileId);

        result.Source.Should().Be(ActiveTemplateSource.Schedule);
        result.TemplateId.Should().Be("vora-velvet");
    }

    // ---------- SetActiveAsync semantics ----------

    [Fact]
    public async Task SetActiveAsync_throws_for_unknown_template_id()
    {
        _registry.Exists("nope").Returns(false);

        var act = async () => await _manager.SetActiveAsync(Guid.NewGuid(), "nope");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Unknown*");
    }

    [Fact]
    public async Task SetActiveAsync_throws_for_empty_template_id()
    {
        var act = async () => await _manager.SetActiveAsync(Guid.NewGuid(), "   ");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*required*");
    }

    [Fact]
    public async Task SetActiveAsync_throws_when_profile_not_found()
    {
        _registry.Exists("vora-noir").Returns(true);
        _users.GetProfileByIdAsync(Arg.Any<Guid>()).Returns((UserProfile?)null);

        var act = async () => await _manager.SetActiveAsync(Guid.NewGuid(), "vora-noir");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Profile not found*");
    }

    [Fact]
    public async Task SetActiveAsync_sets_profile_template_when_no_active_schedule()
    {
        var profileId = Guid.NewGuid();
        var profile = MakeProfile(profileId);
        _registry.Exists("vora-noir").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);

        var response = await _manager.SetActiveAsync(profileId, "vora-noir");

        response.TemplateId.Should().Be("vora-noir");
        response.Source.Should().Be(ActiveTemplateSource.Profile);
        profile.ClientTemplateId.Should().Be("vora-noir");
        profile.ScheduleOverrideTemplateId.Should().BeNull();
        profile.ScheduleOverrideScheduleId.Should().BeNull();
    }

    [Fact]
    public async Task SetActiveAsync_records_override_when_picking_different_template_during_active_schedule()
    {
        var profileId = Guid.NewGuid();
        var profile = MakeProfile(profileId);
        var schedule = MakeSchedule("vora-velvet");
        _registry.Exists("vora-aurora").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(schedule);

        var response = await _manager.SetActiveAsync(profileId, "vora-aurora");

        response.Source.Should().Be(ActiveTemplateSource.Override);
        profile.ScheduleOverrideTemplateId.Should().Be("vora-aurora");
        profile.ScheduleOverrideScheduleId.Should().Be(schedule.Id);
    }

    [Fact]
    public async Task SetActiveAsync_clears_override_when_picking_schedule_template()
    {
        var profileId = Guid.NewGuid();
        var schedule = MakeSchedule("vora-velvet");
        var profile = MakeProfile(
            profileId,
            scheduleOverrideTemplateId: "vora-aurora",
            scheduleOverrideScheduleId: schedule.Id);
        _registry.Exists("vora-velvet").Returns(true);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _schedules.GetActiveScheduleAsync(Arg.Any<DateTime>()).Returns(schedule);

        var response = await _manager.SetActiveAsync(profileId, "vora-velvet");

        response.Source.Should().Be(ActiveTemplateSource.Schedule);
        profile.ScheduleOverrideTemplateId.Should().BeNull();
        profile.ScheduleOverrideScheduleId.Should().BeNull();
    }

    // ---------- ClearActiveAsync ----------

    [Fact]
    public async Task ClearActiveAsync_returns_false_when_profile_missing()
    {
        _users.GetProfileByIdAsync(Arg.Any<Guid>()).Returns((UserProfile?)null);

        var ok = await _manager.ClearActiveAsync(Guid.NewGuid());

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ClearActiveAsync_clears_all_template_fields_and_persists()
    {
        var profileId = Guid.NewGuid();
        var profile = MakeProfile(
            profileId,
            clientTemplateId: "vora-noir",
            scheduleOverrideTemplateId: "vora-aurora",
            scheduleOverrideScheduleId: Guid.NewGuid());
        _users.GetProfileByIdAsync(profileId).Returns(profile);

        var ok = await _manager.ClearActiveAsync(profileId);

        ok.Should().BeTrue();
        profile.ClientTemplateId.Should().BeNull();
        profile.ScheduleOverrideTemplateId.Should().BeNull();
        profile.ScheduleOverrideScheduleId.Should().BeNull();
        await _users.Received(1).UpdateProfileAsync(profile);
    }

    // ---------- SetDefaultAsync ----------

    [Fact]
    public async Task SetDefaultAsync_returns_false_for_blank_template_id()
    {
        (await _manager.SetDefaultAsync("   ")).Should().BeFalse();
    }

    [Fact]
    public async Task SetDefaultAsync_returns_false_for_unknown_template_id()
    {
        _registry.Exists("missing").Returns(false);
        (await _manager.SetDefaultAsync("missing")).Should().BeFalse();
    }

    [Fact]
    public async Task SetDefaultAsync_persists_known_template_and_notifies()
    {
        _registry.Exists("vora-noir").Returns(true);
        var settings = new ServerSetting { DefaultClientTemplateId = "vora-cinema" };
        _settings.GetSettingsForUpdateAsync().Returns(settings);

        var ok = await _manager.SetDefaultAsync("vora-noir");

        ok.Should().BeTrue();
        settings.DefaultClientTemplateId.Should().Be("vora-noir");
        await _settings.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyClientTemplateConfigurationChangedAsync();
    }
}
