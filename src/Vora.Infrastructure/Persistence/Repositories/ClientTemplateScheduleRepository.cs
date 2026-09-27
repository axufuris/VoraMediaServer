using Microsoft.EntityFrameworkCore;
using Vora.Application.Templates;
using Vora.Domain.Entities.Templates;

namespace Vora.Infrastructure.Persistence.Repositories;

public class ClientTemplateScheduleRepository : IClientTemplateScheduleRepository
{
    private readonly VoraDbContext _db;

    public ClientTemplateScheduleRepository(VoraDbContext db)
    {
        _db = db;
    }

    public Task<List<ClientTemplateSchedule>> GetAllAsync() =>
        _db.ClientTemplateSchedules
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync();

    public Task<ClientTemplateSchedule?> GetByIdAsync(Guid id) =>
        _db.ClientTemplateSchedules.FirstOrDefaultAsync(s => s.Id == id);

    public Task<ClientTemplateSchedule?> GetActiveAsync(DateTime nowUtc) =>
        _db.ClientTemplateSchedules
            .Where(s => s.Enabled && s.StartsAtUtc <= nowUtc && s.EndsAtUtc > nowUtc)
            .OrderByDescending(s => s.Priority)
            .ThenByDescending(s => s.StartsAtUtc)
            .FirstOrDefaultAsync();

    public async Task AddAsync(ClientTemplateSchedule schedule)
    {
        await _db.ClientTemplateSchedules.AddAsync(schedule);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(ClientTemplateSchedule schedule)
    {
        schedule.UpdatedAtUtc = DateTime.UtcNow;
        _db.ClientTemplateSchedules.Update(schedule);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var existing = await _db.ClientTemplateSchedules.FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null) return;
        _db.ClientTemplateSchedules.Remove(existing);
        await _db.SaveChangesAsync();
    }
}
