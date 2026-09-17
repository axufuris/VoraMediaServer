using Vora.Domain.Entities.Users;

namespace Vora.Application.Users;

public interface IInvitationRepository
{
    Task CreateAsync(InvitationTicket ticket);
    Task<InvitationTicket?> GetByTokenHashAsync(string tokenHash);
    Task<IReadOnlyList<InvitationTicket>> GetAllActiveAsync();
    Task<bool> DeleteAsync(Guid id);
    Task DeleteByTokenHashAsync(string tokenHash);
    Task InvalidateOutstandingForEmailAsync(string email);
}
