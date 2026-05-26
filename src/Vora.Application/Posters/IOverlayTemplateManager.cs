using Vora.Application.Posters.Dtos;

namespace Vora.Application.Posters;

public interface IOverlayTemplateManager
{
    Task<IReadOnlyList<OverlayTemplateDto>> GetTemplatesAsync(Guid? libraryId);
    Task<OverlayTemplateDto> CreateTemplateAsync(OverlayTemplateDto dto);
    Task<OverlayTemplateDto?> UpdateTemplateAsync(Guid id, OverlayTemplateDto dto);
    Task DeleteTemplateAsync(Guid id);
}
