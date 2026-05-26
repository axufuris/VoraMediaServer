using Vora.Application.Posters.Dtos;
using Vora.Domain.Entities.Posters;

namespace Vora.Application.Posters;

public class OverlayTemplateManager : IOverlayTemplateManager
{
    private readonly IOverlayTemplateRepository _repository;

    public OverlayTemplateManager(IOverlayTemplateRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<OverlayTemplateDto>> GetTemplatesAsync(Guid? libraryId)
    {
        var templates = await _repository.GetTemplatesForLibraryAsync(libraryId ?? Guid.Empty);
        return templates.Select(ToDto).ToList();
    }

    public async Task<OverlayTemplateDto> CreateTemplateAsync(OverlayTemplateDto dto)
    {
        var template = new OverlayTemplate
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? $"{dto.TargetMediaType} Template" : dto.Name,
            TargetMediaType = dto.TargetMediaType,
            TargetLibraryId = dto.TargetLibraryId,
            ConfigurationJson = dto.ConfigurationJson,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.AddTemplateAsync(template);
        return ToDto(template);
    }

    public async Task<OverlayTemplateDto?> UpdateTemplateAsync(Guid id, OverlayTemplateDto dto)
    {
        var existing = await _repository.GetTemplateByIdAsync(id);
        if (existing == null)
        {
            return null;
        }

        existing.Name = string.IsNullOrWhiteSpace(dto.Name) ? existing.Name : dto.Name;
        existing.TargetMediaType = dto.TargetMediaType;
        existing.TargetLibraryId = dto.TargetLibraryId;
        existing.ConfigurationJson = dto.ConfigurationJson;
        existing.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateTemplateAsync(existing);
        return ToDto(existing);
    }

    public Task DeleteTemplateAsync(Guid id) => _repository.DeleteTemplateAsync(id);

    private static OverlayTemplateDto ToDto(OverlayTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        TargetMediaType = template.TargetMediaType,
        TargetLibraryId = template.TargetLibraryId,
        ConfigurationJson = template.ConfigurationJson
    };
}
