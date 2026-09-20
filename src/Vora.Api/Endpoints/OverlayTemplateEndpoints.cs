using Vora.Application.Libraries;
using Vora.Application.Posters;
using Vora.Application.Posters.Dtos;
using Vora.Application.Tasks;

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

    private static async Task<IResult> GetTemplatesAsync(Guid? libraryId, IOverlayTemplateManager manager)
    {
        var dtos = await manager.GetTemplatesAsync(libraryId);
        return Results.Ok(dtos);
    }

    private static async Task<IResult> CreateTemplateAsync(OverlayTemplateDto dto, IOverlayTemplateManager manager)
    {
        var created = await manager.CreateTemplateAsync(dto);
        return Results.Created($"/api/overlays/templates/{created.Id}", created);
    }

    private static async Task<IResult> UpdateTemplateAsync(Guid id, OverlayTemplateDto dto, IOverlayTemplateManager manager)
    {
        var updated = await manager.UpdateTemplateAsync(id, dto);
        return updated == null ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> DeleteTemplateAsync(Guid id, IOverlayTemplateManager manager)
    {
        await manager.DeleteTemplateAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> QueueLibrarySyncAsync(Guid libraryId, ITaskQueueManager taskQueue, ILibraryManager libraryManager)
    {
        var library = await libraryManager.GetLibraryByIdAsync(libraryId);
        taskQueue.QueueGenerateLibraryPosterOverlays(libraryId, library?.Name);
        return Results.Accepted(uri: null, value: new { Message = "Library overlay sync queued." });
    }
}
