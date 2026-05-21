using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Email;

public interface IEmailTemplateRepository
{
    Task<EmailTemplate?> GetOverrideAsync(EmailTemplateKey key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmailTemplate>> GetAllOverridesAsync(CancellationToken cancellationToken = default);
    Task UpsertOverrideAsync(EmailTemplate template, CancellationToken cancellationToken = default);
    Task DeleteOverrideAsync(EmailTemplateKey key, CancellationToken cancellationToken = default);
}
