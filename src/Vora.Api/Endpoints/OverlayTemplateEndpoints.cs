using Vora.Application.Posters;
using Vora.Application.Posters.Dtos;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Posters;

namespace Vora.Api.Endpoints;

public static class OverlayTemplateEndpoints
{
    public static RouteGroupBuilder MapOverlayTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/overlays/templates").WithTags("Overlay Templates").RequireAuthorization("AdminOnly");

        group.MapGet("/{libraryId:guid?}", GetTemplatesAsync);
        group.MapPost("/", CreateTemplateAsync);
        group.MapPut("/{id:guid}", UpdateTemplateAsync);
        group.MapDelete("/{id:guid}", DeleteTemplateAsync);

        group.MapPost("/sync-library/{libraryId:guid}", QueueLibrarySyncAsync);

        return group;
    }

    private static async Task<IResult> GetTemplatesAsync(Guid? libraryId, IOverlayTemplateRepository repo)
    {
        var templates = await repo.GetTemplatesForLibraryAsync(libraryId ?? Guid.Empty);
        var dtos = templates.Select(t => new OverlayTemplateDto
        {
            Id = t.Id,
            Name = t.Name,
            TargetMediaType = t.TargetMediaType,
            TargetLibraryId = t.TargetLibraryId,
            ConfigurationJson = t.ConfigurationJson
        });
        return Results.Ok(dtos);
    }

    private static async Task<IResult> CreateTemplateAsync(OverlayTemplateDto dto, IOverlayTemplateRepository repo)
    {
        var template = new OverlayTemplate
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? $"{dto.TargetMediaType} Template" : dto.Name,
            TargetMediaType = dto.TargetMediaType,
            TargetLibraryId = dto.TargetLibraryId,
            ConfigurationJson = dto.ConfigurationJson,
            UpdatedAt = DateTime.UtcNow
        };

        await repo.AddTemplateAsync(template);
        dto.Id = template.Id;

        return Results.Created($"/api/overlays/templates/{template.Id}", dto);
    }

    private static async Task<IResult> UpdateTemplateAsync(Guid id, OverlayTemplateDto dto, IOverlayTemplateRepository repo)
    {
        var existing = await repo.GetTemplateByIdAsync(id);
        if (existing == null)
        {
            return Results.NotFound();
        }

        existing.Name = string.IsNullOrWhiteSpace(dto.Name) ? existing.Name : dto.Name;
        existing.TargetMediaType = dto.TargetMediaType;
        existing.TargetLibraryId = dto.TargetLibraryId;
        existing.ConfigurationJson = dto.ConfigurationJson;
        existing.UpdatedAt = DateTime.UtcNow;

        await repo.UpdateTemplateAsync(existing);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteTemplateAsync(Guid id, IOverlayTemplateRepository repo)
    {
        await repo.DeleteTemplateAsync(id);
        return Results.NoContent();
    }

    private static IResult QueueLibrarySyncAsync(Guid libraryId, ITaskQueueManager taskQueue)
    {
        taskQueue.QueueGenerateLibraryPosterOverlays(libraryId);
        return Results.Accepted(uri: null, value: new { Message = "Library overlay sync queued." });
    }
}
