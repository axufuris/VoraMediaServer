using Microsoft.EntityFrameworkCore;
using Vora.Application.Email;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class EmailDeliveryLogRepository : IEmailDeliveryLogRepository
{
    private readonly VoraDbContext _dbContext;

    public EmailDeliveryLogRepository(VoraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EmailDeliveryLog> CreateAsync(EmailDeliveryLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.EmailDeliveryLogs.AddAsync(log, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return log;
    }

    public async Task UpdateAsync(Guid id, EmailDeliveryStatus status, int attemptCount, string? errorMessage, DateTime? sentAt, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.EmailDeliveryLogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (row is null) return;
        row.Status = status;
        row.AttemptCount = attemptCount;
        row.ErrorMessage = errorMessage;
        row.SentAt = sentAt;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmailDeliveryLog>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.EmailDeliveryLogs
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Max(1, take))
            .ToListAsync(cancellationToken);
        return rows;
    }
}
