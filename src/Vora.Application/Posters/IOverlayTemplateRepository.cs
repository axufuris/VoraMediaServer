using Vora.Domain.Entities.Posters;

namespace Vora.Application.Posters;

public interface IOverlayTemplateRepository
{
    Task<List<OverlayTemplate>> GetTemplatesForLibraryAsync(Guid libraryId);
    Task<OverlayTemplate?> GetTemplateByIdAsync(Guid id);
    Task AddTemplateAsync(OverlayTemplate template);
    Task UpdateTemplateAsync(OverlayTemplate template);
    Task DeleteTemplateAsync(Guid id);
}