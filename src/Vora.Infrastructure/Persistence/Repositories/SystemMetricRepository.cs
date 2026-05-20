using Microsoft.EntityFrameworkCore;
using Vora.Application.Analysis;
using Vora.Domain.Entities.Tracking;

namespace Vora.Infrastructure.Persistence.Repositories;

public class SystemMetricRepository : ISystemMetricRepository
{
    private readonly VoraDbContext _context;

    public SystemMetricRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task AddMetricAsync(SystemMetric metric)
    {
        await _context.SystemMetrics.AddAsync(metric);
        await _context.SaveChangesAsync();
    }

    public async Task<SystemMetric?> GetLatestMetricAsync()
    {
        return await _context.SystemMetrics
            .AsNoTracking()
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefaultAsync();
    }
}