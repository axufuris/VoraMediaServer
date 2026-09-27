using Microsoft.EntityFrameworkCore;
using Vora.Application.Email;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class EmailTemplateRepository : IEmailTemplateRepository
{
    private readonly VoraDbContext _dbContext;

    public EmailTemplateRepository(VoraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<EmailTemplate?> GetOverrideAsync(EmailTemplateKey key, CancellationToken cancellationToken = default) =>
        _dbContext.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, cancellationToken);

    public async Task<IReadOnlyList<EmailTemplate>> GetAllOverridesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.EmailTemplates.AsNoTracking().ToListAsync(cancellationToken);
        return rows;
    }

    public async Task UpsertOverrideAsync(EmailTemplate template, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Key == template.Key, cancellationToken);
        if (existing is null)
        {
            template.UpdatedAt = DateTime.UtcNow;
            await _dbContext.EmailTemplates.AddAsync(template, cancellationToken);
        }
        else
        {
            existing.SubjectOverride = template.SubjectOverride;
            existing.HtmlBodyOverride = template.HtmlBodyOverride;
            existing.TextBodyOverride = template.TextBodyOverride;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteOverrideAsync(EmailTemplateKey key, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (existing is null) return;
        _dbContext.EmailTemplates.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
