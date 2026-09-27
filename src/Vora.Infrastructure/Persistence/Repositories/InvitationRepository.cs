using Microsoft.EntityFrameworkCore;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class InvitationRepository(VoraDbContext context) : IInvitationRepository
{
    public async Task CreateAsync(InvitationTicket ticket)
    {
        await context.InvitationTickets.AddAsync(ticket);
        await context.SaveChangesAsync();
    }

    public Task<InvitationTicket?> GetByTokenHashAsync(string tokenHash) =>
        context.InvitationTickets
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.ExpiresAt > DateTime.UtcNow);

    public async Task<IReadOnlyList<InvitationTicket>> GetAllActiveAsync()
    {
        var rows = await context.InvitationTickets
            .AsNoTracking()
            .Where(t => t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return rows;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var ticket = await context.InvitationTickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return false;
        context.InvitationTickets.Remove(ticket);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task DeleteByTokenHashAsync(string tokenHash)
    {
        var ticket = await context.InvitationTickets.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        if (ticket is null) return;
        context.InvitationTickets.Remove(ticket);
        await context.SaveChangesAsync();
    }

    public async Task InvalidateOutstandingForEmailAsync(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var outstanding = await context.InvitationTickets
            .Where(t => t.Email.ToLower() == normalized)
            .ToListAsync();
        if (outstanding.Count == 0) return;
        context.InvitationTickets.RemoveRange(outstanding);
        await context.SaveChangesAsync();
    }
}
