using Microsoft.EntityFrameworkCore;
using Vora.Application.Notifications;
using Vora.Domain.Entities.Notifications;

namespace Vora.Infrastructure.Persistence.Repositories;

public class AdminNotificationRepository : IAdminNotificationRepository
{
    private readonly VoraDbContext _context;

    public AdminNotificationRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AdminNotification notification)
    {
        _context.AdminNotifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    public async Task<List<AdminNotification>> GetRecentAsync(int limit, bool unreadOnly)
    {
        var query = _context.AdminNotifications.AsNoTracking().AsQueryable();
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        return await query.OrderByDescending(n => n.CreatedAt).Take(limit).ToListAsync();
    }

    public Task<int> GetUnreadCountAsync() =>
        _context.AdminNotifications.CountAsync(n => !n.IsRead);

    public async Task<bool> MarkReadAsync(Guid id)
    {
        var entity = await _context.AdminNotifications.FindAsync(id);
        if (entity == null) return false;
        if (entity.IsRead) return true;
        entity.IsRead = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task MarkAllReadAsync()
    {
        await _context.AdminNotifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true));
    }

    public async Task ClearAllAsync()
    {
        await _context.AdminNotifications.ExecuteDeleteAsync();
    }
}
