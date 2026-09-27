using Vora.Application.Analysis;
using Vora.Application.Templates;
using Vora.Domain.Entities.Templates;

namespace Vora.Application.Tests.Templates;

public class ClientTemplateScheduleManagerTests
{
    private readonly IClientTemplateScheduleRepository _repo;
    private readonly IClientTemplateRegistry _registry;
    private readonly IClientNotifier _notifier;
    private readonly ClientTemplateScheduleManager _manager;

    public ClientTemplateScheduleManagerTests()
    {
        _repo = Substitute.For<IClientTemplateScheduleRepository>();
        _registry = Substitute.For<IClientTemplateRegistry>();
        _notifier = Substitute.For<IClientNotifier>();
        _manager = new ClientTemplateScheduleManager(_repo, _registry, _notifier);
    }

    private static ClientTemplateSchedule MakeSchedule(Guid id, string templateId = "vora-velvet") => new()
    {
        Id = id,
        TemplateId = templateId,
        Name = "Holiday window",
        StartsAtUtc = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc),
        EndsAtUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        Priority = 0,
        Enabled = true
    };

    [Fact]
    public async Task GetAllAsync_returns_all_schedules_as_view_models()
    {
        var s1 = MakeSchedule(Guid.NewGuid(), "vora-velvet");
        var s2 = MakeSchedule(Guid.NewGuid(), "vora-aurora");
        _repo.GetAllAsync().Returns(new List<ClientTemplateSchedule> { s1, s2 });
        _registry.Exists("vora-velvet").Returns(true);
        _registry.Exists("vora-aurora").Returns(true);

        var result = await _manager.GetAllAsync();

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(s1.Id);
        result[0].TemplateId.Should().Be("vora-velvet");
        result[0].TemplateMissing.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllAsync_flags_template_missing_when_not_in_registry()
    {
        var s = MakeSchedule(Guid.NewGuid(), "deleted-bundle");
        _repo.GetAllAsync().Returns(new List<ClientTemplateSchedule> { s });
        _registry.Exists("deleted-bundle").Returns(false);

        var result = await _manager.GetAllAsync();

        result[0].TemplateMissing.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_repo_returns_null()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>()).Returns((ClientTemplateSchedule?)null);

        (await _manager.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_maps_entity_to_view_model()
    {
        var id = Guid.NewGuid();
        var s = MakeSchedule(id);
        _repo.GetByIdAsync(id).Returns(s);
        _registry.Exists(s.TemplateId).Returns(true);

        var vm = await _manager.GetByIdAsync(id);

        vm.Should().NotBeNull();
        vm!.Id.Should().Be(id);
        vm.Name.Should().Be("Holiday window");
    }

    [Fact]
    public async Task GetActiveScheduleAsync_returns_null_when_repo_returns_null()
    {
        _repo.GetActiveAsync(Arg.Any<DateTime>()).Returns((ClientTemplateSchedule?)null);

        (await _manager.GetActiveScheduleAsync(DateTime.UtcNow)).Should().BeNull();
    }

    [Fact]
    public async Task GetActiveScheduleAsync_filters_out_schedule_whose_template_is_missing()
    {
        var s = MakeSchedule(Guid.NewGuid(), "deleted-bundle");
        _repo.GetActiveAsync(Arg.Any<DateTime>()).Returns(s);
        _registry.Exists("deleted-bundle").Returns(false);

        var result = await _manager.GetActiveScheduleAsync(DateTime.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveScheduleAsync_returns_schedule_when_template_present()
    {
        var s = MakeSchedule(Guid.NewGuid(), "vora-velvet");
        _repo.GetActiveAsync(Arg.Any<DateTime>()).Returns(s);
        _registry.Exists("vora-velvet").Returns(true);

        var result = await _manager.GetActiveScheduleAsync(DateTime.UtcNow);

        result.Should().BeSameAs(s);
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_throws_when_template_id_blank()
    {
        var req = new CreateTemplateScheduleRequest("", "name", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 0, true);

        var act = async () => await _manager.CreateAsync(req);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*TemplateId*required*");
    }

    [Fact]
    public async Task CreateAsync_throws_when_name_blank()
    {
        var req = new CreateTemplateScheduleRequest("vora-velvet", "   ", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 0, true);

        var act = async () => await _manager.CreateAsync(req);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Name*required*");
    }

    [Fact]
    public async Task CreateAsync_throws_when_ends_at_equals_starts_at()
    {
        var start = DateTime.UtcNow;
        var req = new CreateTemplateScheduleRequest("vora-velvet", "n", start, start, 0, true);

        var act = async () => await _manager.CreateAsync(req);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*after*");
    }

    [Fact]
    public async Task CreateAsync_throws_when_ends_at_before_starts_at()
    {
        var start = DateTime.UtcNow;
        var req = new CreateTemplateScheduleRequest("vora-velvet", "n", start, start.AddHours(-1), 0, true);

        var act = async () => await _manager.CreateAsync(req);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*after*");
    }

    [Fact]
    public async Task CreateAsync_persists_and_notifies_for_valid_request()
    {
        var start = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Unspecified);
        var end = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var req = new CreateTemplateScheduleRequest("vora-velvet", "Holiday", start, end, 5, true);
        _registry.Exists("vora-velvet").Returns(true);

        var vm = await _manager.CreateAsync(req);

        await _repo.Received(1).AddAsync(Arg.Is<ClientTemplateSchedule>(s =>
            s.TemplateId == "vora-velvet" &&
            s.Name == "Holiday" &&
            s.Priority == 5 &&
            s.Enabled));
        await _notifier.Received(1).NotifyClientTemplateConfigurationChangedAsync();
        vm.TemplateId.Should().Be("vora-velvet");
    }

    [Fact]
    public async Task CreateAsync_forces_utc_kind_on_start_and_end()
    {
        var localStart = DateTime.SpecifyKind(new DateTime(2026, 11, 25, 12, 0, 0), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(new DateTime(2026, 11, 26, 12, 0, 0), DateTimeKind.Local);
        var req = new CreateTemplateScheduleRequest("vora-velvet", "Holiday", localStart, localEnd, 0, true);
        _registry.Exists("vora-velvet").Returns(true);

        await _manager.CreateAsync(req);

        await _repo.Received(1).AddAsync(Arg.Is<ClientTemplateSchedule>(s =>
            s.StartsAtUtc.Kind == DateTimeKind.Utc &&
            s.EndsAtUtc.Kind == DateTimeKind.Utc));
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_returns_null_when_schedule_not_found()
    {
        var req = new UpdateTemplateScheduleRequest("vora-velvet", "n", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 0, true);
        _repo.GetByIdAsync(Arg.Any<Guid>()).Returns((ClientTemplateSchedule?)null);

        var result = await _manager.UpdateAsync(Guid.NewGuid(), req);

        result.Should().BeNull();
        await _notifier.DidNotReceive().NotifyClientTemplateConfigurationChangedAsync();
    }

    [Fact]
    public async Task UpdateAsync_throws_validation_before_repo_lookup_when_request_invalid()
    {
        var req = new UpdateTemplateScheduleRequest("", "n", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 0, true);

        var act = async () => await _manager.UpdateAsync(Guid.NewGuid(), req);

        await act.Should().ThrowAsync<ArgumentException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task UpdateAsync_writes_back_fields_and_notifies()
    {
        var id = Guid.NewGuid();
        var existing = MakeSchedule(id, "vora-velvet");
        _repo.GetByIdAsync(id).Returns(existing);
        _registry.Exists("vora-aurora").Returns(true);

        var req = new UpdateTemplateScheduleRequest(
            "vora-aurora", "Winter window",
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            10, false);

        var vm = await _manager.UpdateAsync(id, req);

        existing.TemplateId.Should().Be("vora-aurora");
        existing.Name.Should().Be("Winter window");
        existing.Priority.Should().Be(10);
        existing.Enabled.Should().BeFalse();
        await _repo.Received(1).UpdateAsync(existing);
        await _notifier.Received(1).NotifyClientTemplateConfigurationChangedAsync();
        vm.Should().NotBeNull();
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_returns_false_when_schedule_missing()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>()).Returns((ClientTemplateSchedule?)null);

        (await _manager.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        await _notifier.DidNotReceive().NotifyClientTemplateConfigurationChangedAsync();
    }

    [Fact]
    public async Task DeleteAsync_deletes_and_notifies_when_found()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id).Returns(MakeSchedule(id));

        var ok = await _manager.DeleteAsync(id);

        ok.Should().BeTrue();
        await _repo.Received(1).DeleteAsync(id);
        await _notifier.Received(1).NotifyClientTemplateConfigurationChangedAsync();
    }
}
