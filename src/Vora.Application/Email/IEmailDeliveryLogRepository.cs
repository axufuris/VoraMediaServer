using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;

namespace Vora.Application.Email;

public interface IEmailDeliveryLogRepository
{
    Task<EmailDeliveryLog> CreateAsync(EmailDeliveryLog log, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, EmailDeliveryStatus status, int attemptCount, string? errorMessage, DateTime? sentAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmailDeliveryLog>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
    Task<int> PruneOldAsync(int keepCount, CancellationToken cancellationToken = default);
}
