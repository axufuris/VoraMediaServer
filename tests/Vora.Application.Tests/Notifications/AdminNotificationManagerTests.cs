using Vora.Application.Analysis;
using Vora.Application.Notifications;
using Vora.Domain.Entities.Notifications;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Notifications;

public class AdminNotificationManagerTests
{
    private readonly IAdminNotificationRepository _repo;
    private readonly IClientNotifier _notifier;
    private readonly AdminNotificationManager _manager;

    public AdminNotificationManagerTests()
    {
        _repo = Substitute.For<IAdminNotificationRepository>();
        _notifier = Substitute.For<IClientNotifier>();
        _manager = new AdminNotificationManager(_repo, _notifier);
    }

    [Fact]
    public async Task RaiseAsync_persists_notification_and_broadcasts_alert()
    {
        await _manager.RaiseAsync(AdminNotificationSeverity.Warning, "title", "message", contextJson: """{"foo":1}""");

        await _repo.Received(1).AddAsync(Arg.Is<AdminNotification>(n =>
            n.Severity == AdminNotificationSeverity.Warning &&
            n.Title == "title" &&
            n.Message == "message" &&
            n.ContextJson == """{"foo":1}"""));
        await _notifier.Received(1).NotifyAdminAlertAsync("Warning", "title", "message");
    }

    [Fact]
    public async Task RaiseAsync_passes_null_context_when_omitted()
    {
        await _manager.RaiseAsync(AdminNotificationSeverity.Info, "t", "m");

        await _repo.Received(1).AddAsync(Arg.Is<AdminNotification>(n => n.ContextJson == null));
    }

    [Theory]
    [InlineData(AdminNotificationSeverity.Info, "Info")]
    [InlineData(AdminNotificationSeverity.Warning, "Warning")]
    [InlineData(AdminNotificationSeverity.Error, "Error")]
    public async Task RaiseAsync_passes_severity_string_to_client_notifier(AdminNotificationSeverity sev, string expectedString)
    {
        await _manager.RaiseAsync(sev, "t", "m");

        await _notifier.Received(1).NotifyAdminAlertAsync(expectedString, "t", "m");
    }

    [Fact]
    public async Task GetRecentAsync_maps_entities_to_view_models()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _repo.GetRecentAsync(10, false).Returns(new List<AdminNotification>
        {
            new() { Id = id1, Title = "t1", Message = "m1", Severity = AdminNotificationSeverity.Info, IsRead = false },
            new() { Id = id2, Title = "t2", Message = "m2", Severity = AdminNotificationSeverity.Error, IsRead = true }
        });

        var result = await _manager.GetRecentAsync(10, unreadOnly: false);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(id1);
        result[0].Severity.Should().Be("Info");
        result[0].IsRead.Should().BeFalse();
        result[1].Severity.Should().Be("Error");
        result[1].IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentAsync_passes_unread_only_flag_to_repository()
    {
        _repo.GetRecentAsync(Arg.Any<int>(), Arg.Any<bool>()).Returns(new List<AdminNotification>());

        await _manager.GetRecentAsync(50, unreadOnly: true);

        await _repo.Received(1).GetRecentAsync(50, true);
    }

    [Fact]
    public async Task GetUnreadCountAsync_delegates_to_repository()
    {
        _repo.GetUnreadCountAsync().Returns(7);

        (await _manager.GetUnreadCountAsync()).Should().Be(7);
    }

    [Fact]
    public async Task MarkReadAsync_notifies_when_repo_returns_true()
    {
        var id = Guid.NewGuid();
        _repo.MarkReadAsync(id).Returns(true);

        var ok = await _manager.MarkReadAsync(id);

        ok.Should().BeTrue();
        await _notifier.Received(1).NotifyAdminAlertUnreadChangedAsync();
    }

    [Fact]
    public async Task MarkReadAsync_does_not_notify_when_repo_returns_false()
    {
        var id = Guid.NewGuid();
        _repo.MarkReadAsync(id).Returns(false);

        var ok = await _manager.MarkReadAsync(id);

        ok.Should().BeFalse();
        await _notifier.DidNotReceive().NotifyAdminAlertUnreadChangedAsync();
    }

    [Fact]
    public async Task MarkAllReadAsync_persists_and_notifies()
    {
        await _manager.MarkAllReadAsync();

        await _repo.Received(1).MarkAllReadAsync();
        await _notifier.Received(1).NotifyAdminAlertUnreadChangedAsync();
    }
}
