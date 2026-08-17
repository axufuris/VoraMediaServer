using Vora.Domain.Entities.Notifications;

namespace Vora.Application.Notifications;

public interface IAdminNotificationRepository
{
    Task AddAsync(AdminNotification notification);
    Task<List<AdminNotification>> GetRecentAsync(int limit, bool unreadOnly);
    Task<int> GetUnreadCountAsync();
    Task<bool> MarkReadAsync(Guid id);
    Task MarkAllReadAsync();
    Task ClearAllAsync();
}
