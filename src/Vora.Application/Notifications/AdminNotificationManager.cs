using Vora.Application.Analysis;
using Vora.Application.Notifications.ViewModels;
using Vora.Domain.Entities.Notifications;
using Vora.Domain.Enums;

namespace Vora.Application.Notifications;

public interface IAdminNotificationManager
{
    Task RaiseAsync(AdminNotificationSeverity severity, string title, string message, string? contextJson = null);
    Task<List<AdminNotificationVM>> GetRecentAsync(int limit, bool unreadOnly);
    Task<int> GetUnreadCountAsync();
    Task<bool> MarkReadAsync(Guid id);
    Task MarkAllReadAsync();
}

public class AdminNotificationManager : IAdminNotificationManager
{
    private readonly IAdminNotificationRepository _repository;
    private readonly IClientNotifier _notifier;

    public AdminNotificationManager(IAdminNotificationRepository repository, IClientNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task RaiseAsync(AdminNotificationSeverity severity, string title, string message, string? contextJson = null)
    {
        var notification = new AdminNotification
        {
            Severity = severity,
            Title = title,
            Message = message,
            ContextJson = contextJson
        };
        await _repository.AddAsync(notification);
        await _notifier.NotifyAdminAlertAsync(severity.ToString(), title, message);
    }

    public async Task<List<AdminNotificationVM>> GetRecentAsync(int limit, bool unreadOnly)
    {
        var entities = await _repository.GetRecentAsync(limit, unreadOnly);
        return entities.Select(AdminNotificationVM.FromEntity).ToList();
    }

    public Task<int> GetUnreadCountAsync() => _repository.GetUnreadCountAsync();

    public async Task<bool> MarkReadAsync(Guid id)
    {
        var ok = await _repository.MarkReadAsync(id);
        if (ok) await _notifier.NotifyAdminAlertUnreadChangedAsync();
        return ok;
    }

    public async Task MarkAllReadAsync()
    {
        await _repository.MarkAllReadAsync();
        await _notifier.NotifyAdminAlertUnreadChangedAsync();
    }
}
