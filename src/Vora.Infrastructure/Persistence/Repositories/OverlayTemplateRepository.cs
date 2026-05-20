using Microsoft.EntityFrameworkCore;
using Vora.Application.Posters;
using Vora.Domain.Entities.Posters;

namespace Vora.Infrastructure.Persistence.Repositories;

public class OverlayTemplateRepository : IOverlayTemplateRepository
{
    private readonly VoraDbContext _context;

    public OverlayTemplateRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<OverlayTemplate>> GetTemplatesForLibraryAsync(Guid libraryId)
    {
        return await _context.OverlayTemplates
            .AsNoTracking()
            .Where(t => t.TargetLibraryId == null || t.TargetLibraryId == libraryId)
            .ToListAsync();
    }

    public async Task<OverlayTemplate?> GetTemplateByIdAsync(Guid id)
    {
        return await _context.OverlayTemplates
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task AddTemplateAsync(OverlayTemplate template)
    {
        await _context.OverlayTemplates.AddAsync(template);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateTemplateAsync(OverlayTemplate template)
    {
        template.UpdatedAt = DateTime.UtcNow;
        _context.OverlayTemplates.Update(template);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteTemplateAsync(Guid id)
    {
        var template = await _context.OverlayTemplates.FindAsync(id);
        if (template != null)
        {
            _context.OverlayTemplates.Remove(template);
            await _context.SaveChangesAsync();
        }
    }
}